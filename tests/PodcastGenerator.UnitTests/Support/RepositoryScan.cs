namespace PodcastGenerator.UnitTests.Support;

/// <summary>Chooses which repository files the source and key scans in <c>ArchitectureTests</c> may open. The rules come from
/// <c>CLAUDE.md</c> ("Gotchas": no test reads the user's key or key file) and AC-5 and AC-27 of the spec.
/// Paths are judged by their segments, relative to the repository root, so where the repository is cloned does not matter
/// (a clone under <c>/home/robin/</c> still scans everything).</summary>
internal static class RepositoryScan
{
    /// <summary>The folders whose files are scanned, besides the files that sit directly in the repository root.</summary>
    private static readonly string[] ContentRoots = ["src", "tests", "docs", "postman", ".github"];

    /// <summary>Folders that are never entered: build output, git's own data and the agent settings folder (which may hold
    /// credentials, AC-27).</summary>
    private static readonly HashSet<string> SkippedSegments = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".claude" };

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".sln", ".md", ".json", ".yml", ".yaml", ".sh", ".txt", ".xml", ".gitignore", ".gitattributes",
    };

    /// <summary>A file that can hold a key: <c>.env</c>, <c>.env.*</c> and <c>*.env</c> (the user's key file and <c>config.env</c>).</summary>
    public static bool IsKeyFile(string fileName) =>
        fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".env", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a file may be opened by a scan. <paramref name="relativePath"/> is relative to the repository root.</summary>
    public static bool IsScannable(string relativePath)
    {
        var segments = Split(relativePath);
        if (segments.Length == 0 || segments.Any(SkippedSegments.Contains))
        {
            return false;
        }

        var fileName = segments[^1];
        if (IsKeyFile(fileName))
        {
            return false;
        }

        var inContentRoot = segments.Length == 1 || ContentRoots.Contains(segments[0], StringComparer.Ordinal);
        return inContentRoot && Extensions.Contains(Path.GetExtension(fileName));
    }

    /// <summary>The <c>.cs</c> files under <c>src</c>, without build output.</summary>
    public static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(RepositoryFiles.Combine("src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => IsScannable(RelativeToRoot(file)));

    /// <summary>The files that could hold a key. Only the content folders and the root's own files are listed, so
    /// <c>.claude/</c> and <c>.git/</c> are not walked, and key files are never opened.</summary>
    public static IEnumerable<string> ScannedFiles()
    {
        var rootFiles = Directory.EnumerateFiles(RepositoryFiles.Root, "*", SearchOption.TopDirectoryOnly);
        var folderFiles = ContentRoots
            .Select(folder => RepositoryFiles.Combine(folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories));

        return rootFiles.Concat(folderFiles).Where(file => IsScannable(RelativeToRoot(file)));
    }

    private static string RelativeToRoot(string file) => Path.GetRelativePath(RepositoryFiles.Root, file);

    private static string[] Split(string relativePath) =>
        relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
}
