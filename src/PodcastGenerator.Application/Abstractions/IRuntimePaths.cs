namespace PodcastGenerator.Application.Abstractions;

/// <summary>Where the app keeps its runtime files and writes audio by default (CLAUDE.md, "Usage").</summary>
public interface IRuntimePaths
{
    /// <summary><c>&lt;UserProfile&gt;/.config/PodcastGenerator</c>.</summary>
    string RuntimeFolder { get; }

    /// <summary><c>PodcastGenerator.env</c> inside <see cref="RuntimeFolder"/>.</summary>
    string KeyFilePath { get; }

    /// <summary>The editable narration style file inside <see cref="RuntimeFolder"/>.</summary>
    string StyleFilePath { get; }

    /// <summary><c>&lt;UserProfile&gt;/PodcastGenerator</c>.</summary>
    string DefaultOutputDirectory { get; }
}
