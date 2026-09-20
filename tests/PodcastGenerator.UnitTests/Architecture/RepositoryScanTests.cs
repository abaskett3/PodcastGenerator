using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Architecture;

/// <summary>Guards the scans in <see cref="ArchitectureTests"/>: they must look at the files they claim to (AC-8, AC-27) and
/// must never open a key file (AC-5, <c>CLAUDE.md</c> "Gotchas"). Paths are relative to the repository root, so these tests
/// do not depend on where the repository is cloned.</summary>
public class RepositoryScanTests
{
    // Paths are written with "/" here and turned into the platform form.
    private static string Native(string path) => Path.Combine(path.Split('/'));
    private static string P(params string[] parts) => Path.Combine(parts);

    // Files whose names or folders contain the text "git", "bin" or "obj" but are not those folders.
    [Theory]
    [InlineData(".github/workflows/release.yml")]
    [InlineData(".github/scripts/classify-changes.sh")]
    [InlineData(".gitignore")]
    [InlineData(".gitattributes")]
    [InlineData("README.md")]
    [InlineData("PodcastGenerator.sln")]
    [InlineData("src/PodcastGenerator.Cli/Program.cs")]
    [InlineData("src/robin/Habit.cs")]
    [InlineData("src/binder/Binder.cs")]
    [InlineData("tests/objective/Goal.cs")]
    [InlineData("docs/specs/initial-development.md")]
    [InlineData("postman/PodcastGenerator.postman_collection.json")]
    public void Files_that_only_look_like_build_output_or_git_data_are_scanned(string path)
    {
        Assert.True(RepositoryScan.IsScannable(Native(path)), Native(path));
    }

    [Theory]
    [InlineData("src/PodcastGenerator.Cli/bin/Debug/Program.cs")]
    [InlineData("tests/PodcastGenerator.UnitTests/obj/Generated.cs")]
    [InlineData(".git/config")]
    [InlineData(".git/hooks/pre-commit.sh")]
    public void Build_output_and_git_data_are_skipped_by_folder_name(string path)
    {
        Assert.False(RepositoryScan.IsScannable(Native(path)), Native(path));
    }

    // AC-5, CLAUDE.md "Gotchas": no test reads a key file. The key files are PodcastGenerator.env, config.env and the
    // usual .env variants; the .claude folder may hold credentials (AC-27).
    [Theory]
    [InlineData("PodcastGenerator.env")]
    [InlineData("config.env")]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData(".env.production")]
    [InlineData(".claude/settings.json")]
    [InlineData(".claude/credentials/key.json")]
    [InlineData(".claude/skills/deliver/SKILL.md")]
    [InlineData("docs/.env.local")]
    [InlineData("src/config.env")]
    [InlineData("tests/.ENV")]
    public void Key_files_and_the_claude_folder_are_never_scanned(string path)
    {
        Assert.False(RepositoryScan.IsScannable(Native(path)), Native(path));
    }

    [Theory]
    [InlineData(".vs/settings.json")]
    [InlineData("publish/notes.txt")]
    [InlineData("TestResults/log.xml")]
    [InlineData(".idea/workspace.xml")]
    public void Folders_outside_the_content_folders_are_not_walked(string path)
    {
        Assert.False(RepositoryScan.IsScannable(Native(path)), Native(path));
    }

    [Theory]
    [InlineData("PodcastGenerator.env", true)]
    [InlineData("config.env", true)]
    [InlineData(".env", true)]
    [InlineData(".env.local", true)]
    [InlineData(".ENV.Local", true)]
    [InlineData(".environment.md", false)]
    [InlineData("env.md", false)]
    [InlineData(".gitignore", false)]
    public void IsKeyFile_matches_dot_env_variants_and_dot_env_files_only(string name, bool expected)
    {
        Assert.Equal(expected, RepositoryScan.IsKeyFile(name));
    }

    // The scans of the real repository are not empty and reach the files the AC-27 scan says it covers.
    [Fact]
    public void The_scan_of_the_repository_reaches_workflows_scripts_ignore_files_and_source()
    {
        var scanned = RepositoryScan.ScannedFiles().Select(file => Path.GetRelativePath(RepositoryFiles.Root, file)).ToList();

        Assert.Contains(P(".github", "workflows", "release.yml"), scanned);
        Assert.Contains(P(".github", "scripts", "compute-release.sh"), scanned);
        Assert.Contains(".gitignore", scanned);
        Assert.Contains("README.md", scanned);
        Assert.Contains(P("src", "PodcastGenerator.Cli", "Program.cs"), scanned);
        Assert.NotEmpty(RepositoryScan.SourceFiles());
    }

    [Fact]
    public void The_scan_of_the_repository_lists_no_key_file_and_nothing_from_claude_git_or_build_output()
    {
        foreach (var file in RepositoryScan.ScannedFiles().Concat(RepositoryScan.SourceFiles()))
        {
            var relative = Path.GetRelativePath(RepositoryFiles.Root, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            Assert.False(RepositoryScan.IsKeyFile(Path.GetFileName(file)), relative);
            Assert.DoesNotContain(segments, segment => segment is ".claude" or ".git" or "bin" or "obj");
        }
    }
}
