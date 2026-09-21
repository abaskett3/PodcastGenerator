using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Narration;
using PodcastGenerator.Application.Runtime;
using PodcastGenerator.Application.Scripts;
using PodcastGenerator.Domain.Narration;
using PodcastGenerator.Domain.Security;

namespace PodcastGenerator.Application.Generation;

public sealed record GenerationRequest(string ScriptPath, string? OutputPath);

public sealed record GenerationResult(string OutputPath, int ChunkCount);

public interface IPodcastGenerationService
{
    /// <summary>Turns a script file into an MP3 file.</summary>
    /// <exception cref="UserFacingException">Anything the person running the tool can fix or must know about.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled. No partial output remains.</exception>
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken);
}

public sealed class PodcastGenerationService : IPodcastGenerationService
{
    private const string ScriptExtension = ".txt";
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly IFileSystem _fileSystem;
    private readonly IRuntimePaths _paths;
    private readonly IRuntimeInitializer _runtimeInitializer;
    private readonly IStyleProvider _styleProvider;
    private readonly IApiKeyProvider _apiKeyProvider;
    private readonly IScriptParser _parser;
    private readonly TranscriptRenderer _renderer;
    private readonly ChunkPlanner _chunkPlanner;
    private readonly ISpeechClient _speechClient;
    private readonly IAudioWorkspaceFactory _workspaceFactory;
    private readonly OutputPathResolver _outputPathResolver;
    private readonly IDelayer _delayer;
    private readonly IRunReporter _reporter;

    public PodcastGenerationService(
        IFileSystem fileSystem,
        IRuntimePaths paths,
        IRuntimeInitializer runtimeInitializer,
        IStyleProvider styleProvider,
        IApiKeyProvider apiKeyProvider,
        IScriptParser parser,
        TranscriptRenderer renderer,
        ChunkPlanner chunkPlanner,
        ISpeechClient speechClient,
        IAudioWorkspaceFactory workspaceFactory,
        OutputPathResolver outputPathResolver,
        IDelayer delayer,
        IRunReporter reporter)
    {
        _fileSystem = fileSystem;
        _paths = paths;
        _runtimeInitializer = runtimeInitializer;
        _styleProvider = styleProvider;
        _apiKeyProvider = apiKeyProvider;
        _parser = parser;
        _renderer = renderer;
        _chunkPlanner = chunkPlanner;
        _speechClient = speechClient;
        _workspaceFactory = workspaceFactory;
        _outputPathResolver = outputPathResolver;
        _delayer = delayer;
        _reporter = reporter;
    }

    public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Everything that can be checked without the network comes first, so a bad input never costs an API call.
        var target = _outputPathResolver.Resolve(request.OutputPath);
        var scriptText = await ReadScriptAsync(request.ScriptPath, cancellationToken).ConfigureAwait(false);

        var script = _parser.Parse(scriptText);
        if (!script.HasSpeech)
        {
            throw new UserFacingException(
                $"There is nothing to narrate in '{request.ScriptPath}': the script is empty or has only headings, cues and directions.");
        }

        await _runtimeInitializer.EnsureAsync(cancellationToken).ConfigureAwait(false);
        var style = await _styleProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var apiKey = await _apiKeyProvider.FindAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UserFacingException(MissingKeyMessage());

        var plan = _chunkPlanner.Plan(_renderer.Render(script), NarrationSettings.MaxChunkCharacters);
        foreach (var warning in plan.Warnings)
        {
            _reporter.Warning(warning);
        }

        try
        {
            _fileSystem.CreateDirectory(target.Directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UserFacingException($"The output directory '{target.Directory}' could not be created: {exception.Message}", exception);
        }

        var temporaryPath = Path.Combine(target.Directory, $".PodcastGenerator-{Guid.NewGuid():N}.tmp");
        await using var workspace = _workspaceFactory.Create();
        try
        {
            for (var index = 0; index < plan.Chunks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _reporter.ChunkStarted(index + 1, plan.Chunks.Count);
                var audio = await SynthesizeWithRetriesAsync(plan.Chunks[index], plan.Chunks.Count, style, apiKey, cancellationToken).ConfigureAwait(false);
                await workspace.AddChunkAsync(audio, cancellationToken).ConfigureAwait(false);
            }

            await workspace.WriteMp3Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            var outputPath = MoveToFinalPath(temporaryPath, target);
            return new GenerationResult(outputPath, plan.Chunks.Count);
        }
        finally
        {
            // On success the temporary file has been moved away; on failure or cancellation this removes it (AC-14). A file
            // that cannot be removed must not hide the real error, so this is best effort.
            try
            {
                _fileSystem.DeleteFile(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _reporter.Warning($"The temporary file '{temporaryPath}' could not be removed: {exception.Message}");
            }
        }
    }

    private async Task<string> ReadScriptAsync(string scriptPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new UserFacingException("No script file was given.");
        }

        if (!string.Equals(Path.GetExtension(scriptPath), ScriptExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFacingException(
                $"Only '{ScriptExtension}' script files are supported, but '{scriptPath}' is not one. " +
                "Markdown and other formats are not supported.");
        }

        if (!_fileSystem.FileExists(scriptPath))
        {
            throw new UserFacingException($"The script file '{scriptPath}' does not exist.");
        }

        try
        {
            return await _fileSystem.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UserFacingException($"The script file '{scriptPath}' could not be read: {exception.Message}", exception);
        }
    }

    private string MissingKeyMessage() =>
        $"No OpenRouter API key was found. Set it with 'PodcastGenerator --set-config {ApiKeyProvider.KeyName} <key>' " +
        $"(where <key> is your OpenRouter API key). It is saved as the line {ApiKeyProvider.KeyName}=<key> in '{_paths.KeyFilePath}'. " +
        $"The {ApiKeyProvider.KeyName} environment variable also works.";

    private async Task<SpeechAudio> SynthesizeWithRetriesAsync(
        NarrationChunk chunk,
        int total,
        NarrationStyle style,
        ApiKey apiKey,
        CancellationToken cancellationToken)
    {
        var speechRequest = new SpeechRequest(
            NarrationSettings.Model,
            NarrationSettings.Voice,
            style.BuildInput(chunk.Transcript),
            SpeechAudioFormat.Pcm);

        for (var retry = 0; ; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await _speechClient.SynthesizeAsync(speechRequest, apiKey, cancellationToken).ConfigureAwait(false);
            }
            catch (SpeechException exception) when (exception.IsTransient && retry < NarrationSettings.MaxRetries)
            {
                var wait = exception.RetryAfter is { } requested
                    ? TimeSpan.FromTicks(Math.Min(requested.Ticks, MaxRetryDelay.Ticks))
                    : Backoff(retry);
                _reporter.Retrying(chunk.Number, total, retry + 1, NarrationSettings.MaxRetries, Describe(exception, apiKey));
                await _delayer.DelayAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (SpeechException exception)
            {
                var attempts = retry + 1;
                var attemptText = attempts == 1 ? "1 attempt" : $"{attempts} attempts";
                throw new UserFacingException(
                    $"Narration failed for chunk {chunk.Number} of {total} after {attemptText}: {Describe(exception, apiKey)}",
                    exception);
            }
        }
    }

    /// <summary>The wait before retry number <paramref name="retry"/> (from 0) when the server gave no
    /// <c>Retry-After</c>: 2, 4, 8, 16, then 30 seconds.</summary>
    private static TimeSpan Backoff(int retry)
    {
        var seconds = 2d * Math.Pow(2, retry);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }

    /// <summary>The HTTP status and the API's error message, with the API key removed in case a server echoes it.</summary>
    private static string Describe(SpeechException exception, ApiKey apiKey)
    {
        var parts = new List<string>();
        if (exception.StatusCode is { } status)
        {
            parts.Add($"HTTP {status}");
        }

        var detail = string.IsNullOrWhiteSpace(exception.ApiMessage) ? exception.Message : exception.ApiMessage;
        var key = apiKey.Reveal();
        parts.Add(detail.Replace(key, "[redacted]", StringComparison.Ordinal));
        return string.Join(": ", parts);
    }

    private string MoveToFinalPath(string temporaryPath, OutputTarget target)
    {
        // The final name is chosen only now, and the move never overwrites, so a file that appeared in the meantime is
        // never touched (AC-10).
        for (var number = 1; ; number++)
        {
            var candidate = target.PathFor(number);
            if (_fileSystem.FileExists(candidate) || _fileSystem.DirectoryExists(candidate))
            {
                continue;
            }

            try
            {
                _fileSystem.MoveFile(temporaryPath, candidate);
                return candidate;
            }
            catch (IOException) when (_fileSystem.FileExists(candidate) || _fileSystem.DirectoryExists(candidate))
            {
                // Someone created that name after we checked. Try the next one.
            }
        }
    }
}
