using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NLayer;
using PodcastGenerator.Application;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Cli;
using PodcastGenerator.Infrastructure;
using PodcastGenerator.Infrastructure.Audio;
using PodcastGenerator.Infrastructure.FileSystem;
using PodcastGenerator.Infrastructure.Speech;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>One request as the HTTP layer saw it.</summary>
internal sealed record RecordedRequest(
    int Number,
    HttpMethod Method,
    Uri? Uri,
    string? AuthorizationScheme,
    string? AuthorizationParameter,
    string? ContentType,
    string Body)
{
    private const string TranscriptMarker = "#### TRANSCRIPT";

    public JsonElement Json => JsonDocument.Parse(Body).RootElement;

    /// <summary>The <c>input</c> field: the style prompt with the chunk's transcript in it.</summary>
    public string Input => Json.GetProperty("input").GetString()!;

    /// <summary>Everything before the transcript heading: audio profile, scene and director's notes.</summary>
    public string Direction => Input[..Input.IndexOf(TranscriptMarker, StringComparison.Ordinal)];

    /// <summary>The chunk's transcript: what follows the transcript heading of the default style.</summary>
    public string Transcript => Input[(Input.IndexOf(TranscriptMarker, StringComparison.Ordinal) + TranscriptMarker.Length)..].Trim();
}

/// <summary>The fake HTTP layer. Nothing here reaches the network: every request is answered by <see cref="Responder"/>.
/// The default answer is what the OpenRouter speech guide documents for <c>response_format: pcm</c>: raw 16-bit mono PCM
/// bytes, with the content type seen in the capped verification calls of the design doc
/// (<c>audio/pcm; rate=24000; channels=1</c>). The audio itself is a tone made here, because these tests check ordering and
/// joining, not what the model says.</summary>
internal sealed class CannedSpeechHandler : HttpMessageHandler
{
    public static readonly Uri SpeechUrl = new("https://openrouter.ai/api/v1/audio/speech");

    private readonly object _gate = new();
    private int _inFlight;

    public List<RecordedRequest> Requests { get; } = [];

    public int MaxConcurrent { get; private set; }

    /// <summary>Answers a request. May throw to simulate a network failure or a timeout.</summary>
    public Func<RecordedRequest, HttpResponseMessage> Responder { get; set; } =
        request => Audio(Pcm.Tone(300 + (200 * (request.Number - 1)), 0.5));

    public static HttpResponseMessage Audio(byte[] bytes, string contentType = "audio/pcm; rate=24000; channels=1")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    /// <summary>The documented error shape (https://openrouter.ai/docs/api-reference/errors):
    /// <c>{"error": {"code": ..., "message": ...}}</c> with the same HTTP status.</summary>
    public static HttpResponseMessage Error(HttpStatusCode status, string message, TimeSpan? retryAfter = null)
    {
        var body = JsonSerializer.Serialize(new { error = new { code = (int)status, message } });
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        }

        return response;
    }

    /// <summary>A 200 whose headers arrive at once and whose audio body never finishes: the connection stalls after the headers.
    /// Only cancellation ends a read of it.</summary>
    public static HttpResponseMessage StalledBody()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/pcm; rate=24000; channels=1");
        return response;
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Only ReadAsync is expected.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        RecordedRequest recorded;
        lock (_gate)
        {
            recorded = new RecordedRequest(
                Requests.Count + 1,
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content?.Headers.ContentType?.MediaType,
                body);
            Requests.Add(recorded);
            _inFlight++;
            MaxConcurrent = Math.Max(MaxConcurrent, _inFlight);
        }

        try
        {
            if (request.RequestUri != SpeechUrl)
            {
                throw new InvalidOperationException($"The fake HTTP layer only knows {SpeechUrl}, but got {request.RequestUri}.");
            }

            await Task.Yield();
            return Responder(recorded);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
            }
        }
    }
}

internal static class Pcm
{
    public const int Rate = 24000;

    /// <summary>Signed 16-bit little-endian mono samples of a sine tone.</summary>
    public static byte[] Tone(double frequency, double seconds)
    {
        var samples = (int)(seconds * Rate);
        var bytes = new byte[samples * 2];
        for (var index = 0; index < samples; index++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * frequency * index / Rate) * 0.5 * short.MaxValue);
            bytes[index * 2] = (byte)(value & 0xFF);
            bytes[(index * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return bytes;
    }

    public static byte[] Silence(double seconds) => new byte[(int)(seconds * Rate) * 2];
}

/// <summary>Wraps the real MP3 workspace factory so a test can look at every workspace's temporary folder afterwards.</summary>
internal sealed class RecordingWorkspaceFactory : IAudioWorkspaceFactory
{
    public List<Mp3AudioWorkspace> Created { get; } = [];

    public IAudioWorkspace Create()
    {
        var workspace = (Mp3AudioWorkspace)new Mp3AudioWorkspaceFactory().Create();
        Created.Add(workspace);
        return workspace;
    }
}

/// <summary>The decoded output file.</summary>
internal sealed record DecodedMp3(int SampleRate, int Channels, float[] Left, float[] Right)
{
    public double Seconds => Left.Length / (double)SampleRate;

    public static DecodedMp3 Read(string path)
    {
        using var file = new MpegFile(path);
        var left = new List<float>();
        var right = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index + 1 < read; index += 2)
            {
                left.Add(buffer[index]);
                right.Add(buffer[index + 1]);
            }
        }

        return new DecodedMp3(file.SampleRate, file.Channels, left.ToArray(), right.ToArray());
    }

    public int ZeroCrossings(double fromSeconds, double toSeconds)
    {
        var crossings = 0;
        var start = (int)(fromSeconds * SampleRate);
        var end = Math.Min((int)(toSeconds * SampleRate), Left.Length);
        for (var index = start + 1; index < end; index++)
        {
            if ((Left[index - 1] < 0) != (Left[index] < 0))
            {
                crossings++;
            }
        }

        return crossings;
    }
}

/// <summary>The real application wiring (Application and Infrastructure dependency injection, the real CLI runner, the real
/// file system, the real HTTP client pipeline and the real MP3 encoder). The only things replaced are the outside world:
/// the HTTP handler, the user profile folder (a temporary directory), the environment variables, the clock and the
/// waiting between retries. Nothing reads the real key file or the real profile folders (AC-5).</summary>
internal sealed class Pipeline : IDisposable
{
    public static readonly DateTimeOffset FixedNow = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    public Pipeline(bool withKeyFile = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "pg-int-" + Guid.NewGuid().ToString("N"));
        Profile = Path.Combine(Root, "profile");
        ScriptDirectory = Path.Combine(Root, "scripts");
        Directory.CreateDirectory(Profile);
        Directory.CreateDirectory(ScriptDirectory);
        Paths = new RuntimePaths(Profile);
        Clock = new FixedTimeProvider(FixedNow);
        if (withKeyFile)
        {
            WriteKeyFile($"OPENROUTER_API_KEY={TestKeys.Sentinel}\n");
        }
    }

    public string Root { get; }

    /// <summary>The fake user profile folder.</summary>
    public string Profile { get; }

    public string ScriptDirectory { get; }

    public RuntimePaths Paths { get; }

    public TimeProvider Clock { get; set; }

    /// <summary>The longest one speech attempt may take; tests that stall a response make it short.</summary>
    public TimeSpan SpeechTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public CannedSpeechHandler Http { get; } = new();

    public FakeEnvironmentVariables Environment { get; } = new();

    /// <summary>The console, answered from a script. With no answers it behaves like a closed standard input.</summary>
    public FakeConfigPrompter Prompter { get; } = new();

    public FakeDelayer Delayer { get; } = new();

    public RecordingWorkspaceFactory Workspaces { get; } = new();

    public StringWriter Out { get; private set; } = new();

    public StringWriter Err { get; private set; } = new();

    public string DefaultOutputDirectory => Paths.DefaultOutputDirectory;

    public string DefaultOutputPath => Path.Combine(DefaultOutputDirectory, "Podcast-09-19-2026.mp3");

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    public void WriteKeyFile(string contents)
    {
        Directory.CreateDirectory(Paths.RuntimeFolder);
        File.WriteAllText(Paths.KeyFilePath, contents);
    }

    public string WriteScript(string text, string fileName = "episode.txt")
    {
        var path = Path.Combine(ScriptDirectory, fileName);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    public string WriteScriptBytes(byte[] bytes, string fileName = "episode.txt")
    {
        var path = Path.Combine(ScriptDirectory, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void ResetOutput()
    {
        Out = new StringWriter();
        Err = new StringWriter();
    }

    /// <summary>Builds a fresh service provider (like a new process) and runs the command line the way <c>Program</c> does.</summary>
    public async Task<int> RunAsync(string scriptPath, string? outputPath = null, CancellationToken cancellationToken = default)
    {
        var command = Assert.IsType<RunCommand>(CommandLine.Parse(outputPath is null ? [scriptPath] : [scriptPath, outputPath]));

        await using var provider = BuildProvider();
        return await provider.GetRequiredService<CliRunner>().RunAsync(command, Out, Err, cancellationToken);
    }

    /// <summary>Runs <c>--set-config &lt;key&gt; &lt;value&gt;</c> through a fresh service provider, the way <c>Program</c> does.</summary>
    public async Task<int> RunSetConfigAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var command = Assert.IsType<SetConfigCommand>(CommandLine.Parse([CommandLine.SetConfigOption, key, value]));

        await using var provider = BuildProvider();
        return await provider.GetRequiredService<CliRunner>().SetConfigAsync(command, Out, Err, cancellationToken);
    }

    /// <summary>The console the config prompt reads from. When null the scripted <see cref="Prompter"/> is used. A test that
    /// wants the real <c>ConsoleConfigPrompter</c> (with a <c>StringReader</c> for standard input) sets it.</summary>
    public IConfigPrompter? ConfigPrompter { get; set; }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddPodcastGeneratorApplication();
        services.AddPodcastGeneratorInfrastructure();

        // The outside world, replaced. Registrations added last win.
        services.AddSingleton<IRuntimePaths>(Paths);
        services.AddSingleton<IEnvironmentVariables>(Environment);
        services.AddSingleton<IDelayer>(Delayer);
        services.AddSingleton<IAudioWorkspaceFactory>(Workspaces);
        services.AddSingleton(Clock);
        services.AddSingleton(new SpeechClientOptions(SpeechTimeout));
        services.AddHttpClient<ISpeechClient, OpenRouterSpeechClient>().ConfigurePrimaryHttpMessageHandler(() => Http);
        services.AddSingleton<IRunReporter>(new ConsoleRunReporter(Err));
        services.AddSingleton(ConfigPrompter ?? Prompter);
        services.AddSingleton<CliRunner>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>The names of the files and directories directly inside <paramref name="directory"/>, or empty when it
    /// does not exist.</summary>
    public static string[] Entries(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFileSystemEntries(directory).Select(entry => Path.GetFileName(entry)).OfType<string>().Order(StringComparer.Ordinal).ToArray()
            : [];

    /// <summary>True when every temporary workspace folder the runs created has been deleted (AC-14).</summary>
    public bool AllWorkspaceFoldersDeleted() => Workspaces.Created.All(workspace => !Directory.Exists(workspace.WorkingDirectory));

    /// <summary>Every file under the temporary root, other than the key file the test itself wrote, that contains
    /// <paramref name="secret"/> (AC-26).</summary>
    public IEnumerable<string> FilesContaining(string secret)
    {
        var needle = Encoding.UTF8.GetBytes(secret);
        return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(file => !string.Equals(file, Paths.KeyFilePath, StringComparison.Ordinal))
            .Where(file => File.ReadAllBytes(file).AsSpan().IndexOf(needle) >= 0);
    }
}

/// <summary>Reads a script the way <c>docs/script-writing-guide.md</c> defines it, written independently of the product's
/// parser, to give the expected spoken text. It is the reference the wire-level tests compare against.</summary>
internal static class ScriptOracle
{
    private static readonly Regex Speaker = new(@"^[A-Z][A-Z .'’\-]*:\s*", RegexOptions.Compiled);
    private static readonly Regex Header = new(@"^(PROGRAMME|EPISODE|STYLE|CAST):", RegexOptions.Compiled);
    private static readonly Regex Pause = new(@"^\(\s*(BEAT|PAUSE\s*[-–—]\s*[\d.]+\s*SECONDS?)\s*\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ContinuedOrDirection = new(@"^(\(\s*CONT['’]D\s*\)\s*)?(\([A-Z ,'\-]+\)\s*)?", RegexOptions.Compiled);

    /// <summary>The spoken paragraphs of each segment (a segment starts at a <c>###</c> heading), tags and asterisks removed.</summary>
    public static List<List<string>> Segments(string script)
    {
        var segments = new List<List<string>> { new() };
        foreach (var raw in script.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line == "END" || Header.IsMatch(line) || Pause.IsMatch(line) || (line.StartsWith('[') && line.EndsWith(']')))
            {
                continue;
            }

            if (line.StartsWith("###", StringComparison.Ordinal))
            {
                segments.Add([]);
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            var spoken = line;
            var speaker = Speaker.Match(line);
            if (speaker.Success)
            {
                spoken = line[speaker.Length..];
                spoken = spoken[ContinuedOrDirection.Match(spoken).Length..];
            }

            spoken = spoken.Replace("*", string.Empty, StringComparison.Ordinal).Trim();
            if (spoken.Length > 0)
            {
                segments[^1].Add(spoken);
            }
        }

        return segments.Where(segment => segment.Count > 0).ToList();
    }

    public static List<string> Paragraphs(string script) => Segments(script).SelectMany(segment => segment).ToList();

    /// <summary>Removes bracketed tags and collapses whitespace, so request text can be compared with the script's words.</summary>
    public static string WithoutTags(string text) =>
        Normalize(Regex.Replace(text, @"\[[^\]]*\]", " "));

    public static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
