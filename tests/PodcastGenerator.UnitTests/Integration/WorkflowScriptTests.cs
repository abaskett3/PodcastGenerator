using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Runs the helper scripts of the GitHub workflows under bash with representative inputs. The workflows themselves
/// cannot run here; these scripts hold their decisions (which files count as code, which version to release). Expected values
/// come from the spec (AC-62, AC-67 to AC-70), Conventional Commits 1.0.0 and Semantic Versioning 2.0.0, and the ignore list in
/// CLAUDE.md, "CI/CD". Bash is Git Bash on Windows and the system bash on Linux; the CI runners have both. No network.</summary>
public sealed class WorkflowScriptTests
{
    private static string Script(string name) => RepositoryFiles.Combine(".github", "scripts", name);

    private static string FindBash()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Git for Windows' bash, not the WSL launcher that may also be called bash.exe.
            var roots = new[]
            {
                System.Environment.GetEnvironmentVariable("ProgramFiles"),
                System.Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Path.Combine(System.Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty, "Programs"),
            };
            foreach (var root in roots.Where(root => !string.IsNullOrEmpty(root)))
            {
                var candidate = Path.Combine(root!, "Git", "bin", "bash.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            Assert.Fail("Git Bash was not found. Install Git for Windows: the workflow scripts need bash.");
        }

        foreach (var candidate in new[] { "/bin/bash", "/usr/bin/bash" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Fail("bash was not found.");
        return string.Empty;
    }

    private static (int ExitCode, string Output, string Error) RunBash(string script, byte[]? stdin = null, params (string Name, string Value)[] environment)
    {
        var start = new ProcessStartInfo(FindBash())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(script.Replace('\\', '/'));
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (stdin is not null)
        {
            process.StandardInput.BaseStream.Write(stdin);
        }

        process.StandardInput.Close();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The script did not finish within 60 seconds.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult().Replace("\r\n", "\n"), error.GetAwaiter().GetResult().Replace("\r\n", "\n"));
    }

    private static string Classify(params string[] paths)
    {
        var input = Encoding.UTF8.GetBytes(string.Concat(paths.Select(path => path + "\0")));
        var (exitCode, output, error) = RunBash(Script("classify-changes.sh"), input);
        Assert.True(exitCode == 0, $"classify-changes.sh exited {exitCode}: {error}");
        return output.Trim();
    }

    private static (int ExitCode, string Output, string Error) Compute(string subject, string body = "", string tags = "") =>
        RunBash(
            Script("compute-release.sh"),
            null,
            ("COMMIT_SUBJECT", subject),
            ("COMMIT_BODY", body),
            ("EXISTING_TAGS", tags));

    // AC-62, AC-67: only ignored files means no build, test or release.
    [Theory]
    [InlineData("docs/specs/x.md")]
    [InlineData("docs/anything/at/all.txt")]
    [InlineData(".docs/notes.txt")]
    [InlineData(".github/workflows/release.yml")]
    [InlineData(".github/scripts/classify-changes.sh")]
    [InlineData(".claude/settings.json")]
    [InlineData("agent-memory/notes.txt")]
    [InlineData(".gitignore")]
    [InlineData("README.md")]
    [InlineData("src/PodcastGenerator.Cli/NOTES.md")]
    [InlineData("tests/deep/er/still/readme.md")]
    [InlineData("README.MD")]
    [InlineData("docs/Notes.Md")]
    [InlineData("src/x/y.mD")]
    [InlineData("Docs/Program.md")]
    public void A_change_to_only_an_ignored_file_is_not_code(string path)
    {
        Assert.Equal("code=false", Classify(path));
    }

    [Fact]
    public void A_change_to_only_ignored_files_of_several_kinds_is_not_code()
    {
        Assert.Equal("code=false", Classify("docs/a.md", ".github/workflows/pull-request.yml", ".gitignore", "CLAUDE.md", "docs/designs/x.md"));
    }

    // AC-63: at least one file outside the list is code, also when ignored files changed in the same change.
    [Theory]
    [InlineData("src/PodcastGenerator.Cli/Program.cs")]
    [InlineData("tests/PodcastGenerator.UnitTests/A.cs")]
    [InlineData("PodcastGenerator.sln")]
    [InlineData("Directory.Build.props")]
    [InlineData("postman/PodcastGenerator.postman_collection.json")]
    [InlineData("src/docs/Generated.cs")]
    [InlineData("docs-old/Generated.cs")]
    [InlineData("mydocs/x.cs")]
    [InlineData("sub/.gitignore")]
    [InlineData(".gitignore.bak")]
    [InlineData("file with spaces.cs")]
    // Directory names and .gitignore are matched exactly (Linux file names are case-sensitive); only the .md extension is not.
    [InlineData("Docs/Program.cs")]
    [InlineData("DOCS/x.cs")]
    [InlineData(".GitHub/workflows/release.yml")]
    [InlineData(".Docs/notes.txt")]
    [InlineData(".Claude/settings.json")]
    [InlineData("Agent-Memory/notes.txt")]
    [InlineData(".Gitignore")]
    public void A_change_to_a_file_outside_the_ignore_list_is_code(string path)
    {
        Assert.Equal("code=true", Classify(path));
    }

    [Fact]
    public void One_code_file_among_ignored_files_makes_the_whole_change_code()
    {
        Assert.Equal("code=true", Classify("docs/a.md", "README.md", "src/PodcastGenerator.Domain/Scripts/Script.cs", ".github/workflows/release.yml"));
    }

    // AC-68, AC-69: the release type and the version. Tag list is one per line.
    [Theory]
    [InlineData("feat: add a flag", "", "", "minor", "1.0.0")]
    [InlineData("fix: correct a name", "", "", "patch", "1.0.0")]
    [InlineData("feat(cli): add a flag", "", "v1.2.3", "minor", "1.3.0")]
    [InlineData("fix(cli): correct a name", "", "v1.2.3", "patch", "1.2.4")]
    [InlineData("feat: add a flag (#12)", "", "v1.2.3", "minor", "1.3.0")]
    [InlineData("feat!: remove an argument", "", "v1.2.3", "major", "2.0.0")]
    [InlineData("fix(api)!: change the output", "", "v1.2.3", "major", "2.0.0")]
    [InlineData("refactor: tidy up", "BREAKING CHANGE: the output format changed", "v1.2.3", "major", "2.0.0")]
    [InlineData("feat: add a flag", "Some details.\n\nBREAKING CHANGE: the output format changed", "v1.2.3", "major", "2.0.0")]
    [InlineData("fix: correct a name", "Details.\n\nBREAKING-CHANGE: the output format changed", "v1.2.3", "major", "2.0.0")]
    [InlineData("FEAT: add a flag", "", "v1.2.3", "minor", "1.3.0")]
    public void The_bump_follows_the_conventional_commit_and_the_semantic_version_rules(string subject, string body, string tags, string bump, string version)
    {
        var (exitCode, output, error) = Compute(subject, body, tags);

        Assert.True(exitCode == 0, error);
        Assert.Equal($"bump={bump}\nversion={version}\n", output);
    }

    // AC-68: a commit with none of these creates no release.
    [Theory]
    [InlineData("refactor: tidy up")]
    [InlineData("test: add tests")]
    [InlineData("chore: update a dependency")]
    [InlineData("ci: change a workflow")]
    [InlineData("docs: fix a typo")]
    [InlineData("build(deps): bump a package")]
    [InlineData("Update the readme")]
    [InlineData("feat add a flag")]
    [InlineData("")]
    public void A_commit_that_is_not_feat_fix_or_breaking_publishes_nothing(string subject)
    {
        var (exitCode, output, error) = Compute(subject, "", "v1.2.3");

        Assert.True(exitCode == 0, error);
        Assert.Equal("bump=none\n", output);
    }

    // Conventional Commits 1.0.0: BREAKING CHANGE must be a footer token, upper case. Prose is not a footer.
    [Fact]
    public void A_sentence_that_only_mentions_a_breaking_change_is_not_a_breaking_footer()
    {
        var (exitCode, output, error) = Compute("feat: add a flag", "This is not a breaking change: it only adds a flag.\nSee the BREAKING CHANGE: text in the docs.", "v1.2.3");

        Assert.True(exitCode == 0, error);
        Assert.Equal("bump=minor\nversion=1.3.0\n", output);
    }

    // AC-69: the latest tag is the highest by semantic version precedence, not the last one listed or the alphabetical one.
    [Fact]
    public void The_latest_tag_is_the_highest_by_semantic_version_precedence()
    {
        var (exitCode, output, error) = Compute("feat: add a flag", "", "v1.9.9\nv1.10.0\nv1.2.0\nv0.99.99");

        Assert.True(exitCode == 0, error);
        Assert.Equal("bump=minor\nversion=1.11.0\n", output);
    }

    [Fact]
    public void A_patch_bump_of_a_two_digit_patch_counts_numerically()
    {
        var (_, output, _) = Compute("fix: correct a name", "", "v0.9.9");

        Assert.Equal("bump=patch\nversion=0.9.10\n", output);
    }

    // AC-69: the first release is 1.0.0 with no bump applied, for any releasing type.
    [Theory]
    [InlineData("feat: first feature")]
    [InlineData("fix: first fix")]
    [InlineData("feat!: first breaking")]
    public void With_no_tag_the_first_release_is_1_0_0(string subject)
    {
        var (exitCode, output, error) = Compute(subject);

        Assert.True(exitCode == 0, error);
        Assert.EndsWith("version=1.0.0\n", output, StringComparison.Ordinal);
    }

    // AC-69: a v* tag that is not vMAJOR.MINOR.PATCH fails and names the tag.
    [Theory]
    [InlineData("v1.2")]
    [InlineData("vNext")]
    [InlineData("v1.1.0-rc.1")]
    [InlineData("v01.2.3")]
    [InlineData("v1.2.3.4")]
    public void A_v_tag_that_is_not_MAJOR_MINOR_PATCH_fails_naming_the_tag(string badTag)
    {
        var (exitCode, output, error) = Compute("feat: add a flag", "", $"v1.0.0\n{badTag}");

        Assert.Equal(1, exitCode);
        Assert.Contains(badTag, error, StringComparison.Ordinal);
        Assert.DoesNotContain("version=", output, StringComparison.Ordinal);
    }

    // AC-69: a malformed v* tag fails the run whether or not this merge would release (the AC has no condition).
    [Theory]
    [InlineData("chore: update a dependency")]
    [InlineData("docs: fix a typo")]
    [InlineData("refactor: tidy up")]
    [InlineData("Update the readme")]
    [InlineData("")]
    public void A_v_tag_that_is_not_MAJOR_MINOR_PATCH_fails_naming_the_tag_even_when_the_merge_would_not_release(string subject)
    {
        var (exitCode, output, error) = Compute(subject, "", "v1.0.0\nvNext\nv1.2.0");

        Assert.Equal(1, exitCode);
        Assert.Contains("vNext", error, StringComparison.Ordinal);
        Assert.DoesNotContain("bump=", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merge_that_does_not_release_with_only_valid_tags_still_succeeds_with_no_version()
    {
        var (exitCode, output, error) = Compute("chore: update a dependency", "", "v1.0.0\nv1.2.0");

        Assert.True(exitCode == 0, error);
        Assert.Equal("bump=none\n", output);
    }

    // AC-70: every released version is valid SemVer without a build number and is strictly greater than the previous one.
    [Theory]
    [InlineData("feat: a", "", "v1.0.0")]
    [InlineData("fix: a", "", "v1.0.0")]
    [InlineData("feat!: a", "", "v1.0.0")]
    [InlineData("feat: a", "", "v0.4.2")]
    [InlineData("fix: a", "", "v2.14.7\nv2.3.0")]
    public void The_version_is_plain_semver_and_greater_than_the_previous_release(string subject, string body, string tags)
    {
        var (exitCode, output, error) = Compute(subject, body, tags);

        Assert.True(exitCode == 0, error);
        var version = Regex.Match(output, @"^version=(.+)$", RegexOptions.Multiline).Groups[1].Value;
        Assert.Matches(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", version);
        var latest = tags.Split('\n').Select(tag => Version.Parse(tag[1..])).Max()!;
        Assert.True(Version.Parse(version) > latest, $"{version} must be greater than {latest}.");
    }

    // AC-64: the pattern in the title workflow, read from the file and run under bash, accepts conventional commit titles
    // (scopes and "!" included) and rejects the rest (Conventional Commits 1.0.0: type, optional scope, optional "!", colon,
    // space, description).
    [Theory]
    [InlineData("feat: add a flag", true)]
    [InlineData("fix: correct a name", true)]
    [InlineData("feat(cli): add a flag", true)]
    [InlineData("fix!: change the output", true)]
    [InlineData("feat(cli)!: change the output", true)]
    [InlineData("chore(deps): bump a package", true)]
    [InlineData("refactor: tidy up", true)]
    [InlineData("Feat: capitalised type", true)]
    [InlineData("Add a flag", false)]
    [InlineData("feat:no space", false)]
    [InlineData("feat: ", false)]
    [InlineData("feat : space before colon", false)]
    [InlineData("feat(): empty scope", false)]
    [InlineData("(cli): no type", false)]
    [InlineData(": no type", false)]
    [InlineData("", false)]
    public void The_pull_request_title_pattern_accepts_conventional_commits_only(string title, bool valid)
    {
        var yaml = File.ReadAllText(RepositoryFiles.Combine(".github", "workflows", "pull-request-title.yml"));
        var pattern = Regex.Match(yaml, @"(?m)^\s*pattern='(?<p>[^']+)'").Groups["p"].Value;
        Assert.NotEmpty(pattern);

        var directory = Path.Combine(Path.GetTempPath(), "pg-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "check.sh");
            File.WriteAllText(script, $"#!/usr/bin/env bash\npattern='{pattern}'\n[[ \"$PR_TITLE\" =~ $pattern ]]\n", new UTF8Encoding(false));

            var (exitCode, _, error) = RunBash(script, null, ("PR_TITLE", title));

            Assert.True(exitCode is 0 or 1, error);
            Assert.Equal(valid, exitCode == 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
