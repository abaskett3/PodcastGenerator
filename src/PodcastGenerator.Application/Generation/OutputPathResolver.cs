using System.Globalization;
using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Application.Generation;

/// <summary>Where the audio file goes: a directory, the file name without a numeric suffix, and the extension.</summary>
public sealed record OutputTarget(string Directory, string BaseName, string Extension)
{
    /// <summary>The path for attempt <paramref name="number"/>: <c>Name.mp3</c>, then <c>Name-2.mp3</c>, <c>Name-3.mp3</c>,
    /// and so on (AC-10).</summary>
    public string PathFor(int number) =>
        Path.Combine(Directory, number <= 1 ? string.Concat(BaseName, Extension) : string.Concat(BaseName, "-", number.ToString(CultureInfo.InvariantCulture), Extension));
}

/// <summary>Works out the output location from the optional command-line path (AC-8 to AC-13).</summary>
public sealed class OutputPathResolver
{
    /// <summary>The only output format (CLAUDE.md, "Usage").</summary>
    public const string Mp3Extension = ".mp3";

    private readonly IFileSystem _fileSystem;
    private readonly IRuntimePaths _paths;
    private readonly TimeProvider _timeProvider;

    public OutputPathResolver(IFileSystem fileSystem, IRuntimePaths paths, TimeProvider timeProvider)
    {
        _fileSystem = fileSystem;
        _paths = paths;
        _timeProvider = timeProvider;
    }

    /// <exception cref="UserFacingException">The path is a file path whose extension is not <c>.mp3</c>.</exception>
    public OutputTarget Resolve(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return new OutputTarget(_paths.DefaultOutputDirectory, DefaultBaseName(), Mp3Extension);
        }

        var fullPath = Path.GetFullPath(requestedPath);
        if (_fileSystem.DirectoryExists(fullPath))
        {
            return new OutputTarget(fullPath, DefaultBaseName(), Mp3Extension);
        }

        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, Mp3Extension, StringComparison.OrdinalIgnoreCase))
        {
            var found = extension.Length == 0 ? "no extension" : $"the extension '{extension}'";
            throw new UserFacingException(
                $"The output path '{requestedPath}' has {found}. The output file must have the extension '{Mp3Extension}' " +
                "(only MP3 is supported), or give an existing directory.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Path.GetFullPath(".");
        }

        return new OutputTarget(directory, Path.GetFileNameWithoutExtension(fullPath), extension);
    }

    /// <summary>The default file name without extension: <c>Podcast-MM-DD-YYYY</c> for the local date (AC-9).</summary>
    private string DefaultBaseName() =>
        string.Concat("Podcast-", _timeProvider.GetLocalNow().ToString("MM-dd-yyyy", CultureInfo.InvariantCulture));
}
