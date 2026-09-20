using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Infrastructure.FileSystem;

/// <summary>The runtime folder and default output directory, built from the user profile folder with
/// <see cref="Path.Combine(string, string)"/> so they work on Windows and Linux (AC-8, AC-22).</summary>
public sealed class RuntimePaths : IRuntimePaths
{
    /// <summary>The file that holds the API key, inside the runtime folder.</summary>
    public const string KeyFileName = "PodcastGenerator.env";

    /// <summary>The editable narration style file, inside the runtime folder.</summary>
    public const string StyleFileName = "style.md";

    public RuntimePaths(string userProfileFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileFolder);
        RuntimeFolder = Path.Combine(userProfileFolder, ".config", "PodcastGenerator");
        KeyFilePath = Path.Combine(RuntimeFolder, KeyFileName);
        StyleFilePath = Path.Combine(RuntimeFolder, StyleFileName);
        DefaultOutputDirectory = Path.Combine(userProfileFolder, "PodcastGenerator");
    }

    /// <summary>Uses <see cref="Environment.SpecialFolder.UserProfile"/> of the current user.</summary>
    public static RuntimePaths ForCurrentUser() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public string RuntimeFolder { get; }

    public string KeyFilePath { get; }

    public string StyleFilePath { get; }

    public string DefaultOutputDirectory { get; }
}
