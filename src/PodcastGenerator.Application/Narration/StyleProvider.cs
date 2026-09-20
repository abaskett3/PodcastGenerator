using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Application.Narration;

/// <summary>The narration prompt from the style file. <see cref="TranscriptPlaceholder"/> marks where the transcript of a
/// chunk goes.</summary>
public sealed record NarrationStyle(string Template)
{
    /// <summary>The placeholder the style file must contain exactly once.</summary>
    public const string TranscriptPlaceholder = "{transcript}";

    /// <summary>Builds the <c>input</c> of one request: the same audio profile, scene and director's notes, then the
    /// transcript of the chunk (docs/style-guide.md, section 9.1).</summary>
    public string BuildInput(string transcript) => Template.Replace(TranscriptPlaceholder, transcript, StringComparison.Ordinal);
}

public interface IStyleProvider
{
    /// <summary>Reads the style file from the runtime folder. Called on every run, so an edit takes effect without a
    /// rebuild (AC-43).</summary>
    /// <exception cref="UserFacingException">The file is missing or unusable (AC-44).</exception>
    Task<NarrationStyle> LoadAsync(CancellationToken cancellationToken);
}

public sealed class StyleProvider : IStyleProvider
{
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimePaths _paths;

    public StyleProvider(IFileSystem fileSystem, IRuntimePaths paths)
    {
        _fileSystem = fileSystem;
        _paths = paths;
    }

    public async Task<NarrationStyle> LoadAsync(CancellationToken cancellationToken)
    {
        var path = _paths.StyleFilePath;
        if (!_fileSystem.FileExists(path))
        {
            throw Unusable(path, "it does not exist");
        }

        string template;
        try
        {
            template = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Unusable(path, $"it could not be read ({exception.GetType().Name})");
        }

        var placeholders = CountOccurrences(template, NarrationStyle.TranscriptPlaceholder);
        if (placeholders != 1)
        {
            throw Unusable(
                path,
                placeholders == 0
                    ? $"it does not contain the transcript placeholder {NarrationStyle.TranscriptPlaceholder}"
                    : $"it contains the transcript placeholder {NarrationStyle.TranscriptPlaceholder} {placeholders} times; it must appear exactly once");
        }

        return new NarrationStyle(template);
    }

    private static UserFacingException Unusable(string path, string reason) =>
        new($"The style file '{path}' cannot be used: {reason}. Fix the file, or delete it and run the tool again to restore the default.");

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
