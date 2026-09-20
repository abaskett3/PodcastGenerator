using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Runs the real "Decide whether to release and which version" step of <c>.github/workflows/release.yml</c> (the
/// <c>run:</c> block is read from the file and executed under bash) in a temporary git repository with the two helper scripts,
/// real commits and real tags. The step is what decides AC-66 to AC-70 and AC-73's tag check for a push to <c>main</c>. It
/// runs in a temporary repository only: no network, no GitHub, nothing outside the temporary folders. The rest of the
/// workflow (checkout, build, publish, tag push, release) cannot run here.</summary>
public sealed class ReleasePlanTests : IDisposable
{
    private const string NoBefore = "0000000000000000000000000000000000000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pg-plan-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly string _outputFile;

    public ReleasePlanTests()
    {
        _repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(_repo, ".github", "scripts"));
        foreach (var script in new[] { "classify-changes.sh", "compute-release.sh" })
        {
            File.Copy(RepositoryFiles.Combine(".github", "scripts", script), Path.Combine(_repo, ".github", "scripts", script));
        }

        _outputFile = Path.Combine(_root, "github-output.txt");
        Git("init", "-q", "-b", "main");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal); // git objects are read-only
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private static string FindBash()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var root in new[] { System.Environment.GetEnvironmentVariable("ProgramFiles"), System.Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
            {
                var candidate = Path.Combine(root ?? string.Empty, "Git", "bin", "bash.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            Assert.Fail("Git Bash was not found. Install Git for Windows: the workflow steps need bash and git.");
        }

        return File.Exists("/bin/bash") ? "/bin/bash" : "/usr/bin/bash";
    }

    private static string Slash(string path) => path.Replace('\\', '/');

    private (int ExitCode, string Output, string Error) Run(string fileName, IEnumerable<string> arguments, params (string Name, string Value)[] environment)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = _repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_AUTHOR_NAME"] = "Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = "test@example.invalid";
        start.Environment["GIT_COMMITTER_NAME"] = "Test";
        start.Environment["GIT_COMMITTER_EMAIL"] = "test@example.invalid";
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{fileName} did not finish within 60 seconds.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult().Replace("\r\n", "\n"), error.GetAwaiter().GetResult().Replace("\r\n", "\n"));
    }

    private string Git(params string[] arguments)
    {
        var (exitCode, output, error) = Run("git", ["-c", "core.autocrlf=false", "-c", "commit.gpgsign=false", .. arguments]);
        Assert.True(exitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output.Trim();
    }

    /// <summary>Commits the files (path to text) with a subject and an optional body, and returns the new commit.</summary>
    private string Commit(string subject, string? body, params (string Path, string Text)[] files)
    {
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(_repo, Path.Combine(path.Split('/')));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        Git("add", "-A", "--", ".", ":!changed-files.bin", ":!release-error.txt");
        if (body is null)
        {
            Git("commit", "-q", "-m", subject);
        }
        else
        {
            Git("commit", "-q", "-m", subject, "-m", body);
        }

        return Git("rev-parse", "HEAD");
    }

    private void Tag(string name) => Git("tag", name);

    /// <summary>The <c>run:</c> block of the step with <c>id: plan</c>, dedented.</summary>
    private static string PlanStepScript()
    {
        var lines = File.ReadAllText(RepositoryFiles.Combine(".github", "workflows", "release.yml")).ReplaceLineEndings("\n").Split('\n');
        var id = Array.FindIndex(lines, line => line.Trim() == "id: plan");
        Assert.True(id >= 0, "The step with id: plan was not found.");
        var run = Array.FindIndex(lines, id, line => line.Trim() == "run: |");
        Assert.True(run > id, "The plan step has no run block.");
        var indent = lines[run + 1].Length - lines[run + 1].TrimStart().Length;
        var block = new List<string>();
        for (var index = run + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Trim().Length == 0)
            {
                block.Add(string.Empty);
                continue;
            }

            if (line.Length - line.TrimStart().Length < indent)
            {
                break;
            }

            block.Add(line[indent..]);
        }

        return string.Join('\n', block) + "\n";
    }

    private (int ExitCode, Dictionary<string, string> Outputs, string Output, string Error) RunPlan(string beforeSha)
    {
        File.WriteAllText(_outputFile, string.Empty);
        var script = Path.Combine(_root, "plan.sh");
        File.WriteAllText(script, PlanStepScript(), new UTF8Encoding(false));

        var (exitCode, output, error) = Run(FindBash(), [Slash(script)], ("BEFORE_SHA", beforeSha), ("GITHUB_OUTPUT", Slash(_outputFile)));

        var outputs = File.ReadAllLines(_outputFile)
            .Where(line => line.Contains('='))
            .ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..].Trim());
        return (exitCode, outputs, output, error);
    }

    private string InitialCodeCommit() => Commit("chore: start", null, ("src/Program.cs", "// v1"), ("README.md", "# x"), ("docs/a.md", "a"));

    // AC-67: only ignored files changed means no release, whatever the commit says.
    [Fact]
    public void A_push_that_changes_only_ignored_files_publishes_nothing_even_with_a_feat_subject()
    {
        var before = InitialCodeCommit();
        var head = Commit("feat: describe the tool", null, ("docs/a.md", "changed"), ("README.md", "# changed"), (".github/workflows/x.yml", "name: x"));

        var (exitCode, outputs, output, _) = RunPlan(before);

        Assert.Equal(0, exitCode);
        Assert.Equal("false", outputs["release"]);
        Assert.False(outputs.ContainsKey("version"));
        Assert.Equal(head, outputs["sha"]);
        Assert.Contains("Only ignored files changed", output, StringComparison.Ordinal);
    }

    // AC-68, AC-69: the first release is 1.0.0 for a code change with a releasing subject.
    [Fact]
    public void The_first_feat_that_changes_code_releases_1_0_0_from_the_pushed_commit()
    {
        var before = InitialCodeCommit();
        var head = Commit("feat: add a flag (#3)", null, ("src/Program.cs", "// v2"));

        var (exitCode, outputs, _, error) = RunPlan(before);

        Assert.True(exitCode == 0, error);
        Assert.Equal("true", outputs["release"]);
        Assert.Equal("1.0.0", outputs["version"]);
        Assert.Equal(head, outputs["sha"]);
    }

    [Fact]
    public void A_fix_bumps_the_patch_of_the_highest_tag()
    {
        var before = InitialCodeCommit();
        Tag("v1.2.3");
        Tag("v1.10.0");
        Tag("v1.9.9");
        Commit("fix(cli): correct a name (#4)", null, ("src/Program.cs", "// v2"));

        var (exitCode, outputs, _, error) = RunPlan(before);

        Assert.True(exitCode == 0, error);
        Assert.Equal("true", outputs["release"]);
        Assert.Equal("1.10.1", outputs["version"]);
    }

    [Fact]
    public void A_breaking_change_footer_in_the_body_bumps_the_major()
    {
        var before = InitialCodeCommit();
        Tag("v1.4.0");
        Commit("refactor: change the output format (#5)", "* refactor: change the output format\n\nBREAKING CHANGE: the output is now stereo", ("src/Program.cs", "// v2"));

        var (exitCode, outputs, _, error) = RunPlan(before);

        Assert.True(exitCode == 0, error);
        Assert.Equal("true", outputs["release"]);
        Assert.Equal("2.0.0", outputs["version"]);
    }

    // AC-68: refactor, test, chore and the like publish nothing.
    [Theory]
    [InlineData("chore: update a dependency (#6)")]
    [InlineData("test: add tests (#7)")]
    [InlineData("Update the code")]
    public void A_code_change_whose_subject_is_not_feat_fix_or_breaking_publishes_nothing(string subject)
    {
        var before = InitialCodeCommit();
        Tag("v1.0.0");
        Commit(subject, null, ("src/Program.cs", "// v2"));

        var (exitCode, outputs, _, error) = RunPlan(before);

        Assert.True(exitCode == 0, error);
        Assert.Equal("false", outputs["release"]);
        Assert.False(outputs.ContainsKey("version"));
    }

    // AC-63/AC-67 boundary: one code file among ignored files is a code change.
    [Fact]
    public void A_push_with_docs_and_code_together_is_a_code_change_and_releases()
    {
        var before = InitialCodeCommit();
        Commit("feat: add a flag and document it (#8)", null, ("docs/a.md", "changed"), ("src/Program.cs", "// v2"));

        var (exitCode, outputs, _, error) = RunPlan(before);

        Assert.True(exitCode == 0, error);
        Assert.Equal("true", outputs["release"]);
    }

    // The before-commit is unknown or all zeros (for example the very first push): the pushed commit's own files are used.
    [Fact]
    public void When_the_before_commit_is_all_zeros_a_docs_only_commit_publishes_nothing_and_a_code_commit_releases()
    {
        Commit("docs: first", null, ("docs/a.md", "a"), ("README.md", "# x"));
        var (docsExit, docsOutputs, _, docsError) = RunPlan(NoBefore);
        Assert.True(docsExit == 0, docsError);
        Assert.Equal("false", docsOutputs["release"]);

        Commit("feat: first code", null, ("src/Program.cs", "// v1"));
        var (codeExit, codeOutputs, _, codeError) = RunPlan(NoBefore);
        Assert.True(codeExit == 0, codeError);
        Assert.Equal("true", codeOutputs["release"]);
        Assert.Equal("1.0.0", codeOutputs["version"]);
    }

    [Fact]
    public void When_the_before_commit_does_not_exist_the_pushed_commits_own_files_are_used()
    {
        InitialCodeCommit();
        Commit("docs: more docs", null, ("docs/a.md", "changed"));

        var (exitCode, outputs, _, error) = RunPlan("1234567890123456789012345678901234567890");

        Assert.True(exitCode == 0, error);
        Assert.Equal("false", outputs["release"]);
    }

    // AC-69, and its interaction with the ignore list (AC-67): the malformed tag check has no condition, so it runs even when
    // only ignored files changed. The run then fails (a red release run on main) instead of ending as "nothing to release".
    // No tag and no release are created in either case, so AC-67 (no tag, no release) still holds.
    [Theory]
    [InlineData("vNext")]
    [InlineData("v1.2")]
    [InlineData("v1.1.0-rc.1")]
    public void A_malformed_v_tag_fails_the_plan_even_when_only_ignored_files_changed_and_creates_no_release(string badTag)
    {
        var before = InitialCodeCommit();
        Tag("v1.0.0");
        Tag(badTag);
        Commit("docs: fix a typo (#9)", null, ("docs/a.md", "changed"));

        var (exitCode, outputs, output, error) = RunPlan(before);

        Assert.Equal(1, exitCode);
        Assert.Contains(badTag, output + error, StringComparison.Ordinal);
        Assert.False(outputs.ContainsKey("release"), "A failed plan must not say whether to release.");
        Assert.False(outputs.ContainsKey("version"));
    }

    [Fact]
    public void A_malformed_v_tag_also_fails_a_code_push_that_would_release()
    {
        var before = InitialCodeCommit();
        Tag("vNext");
        Commit("feat: add a flag (#10)", null, ("src/Program.cs", "// v2"));

        var (exitCode, outputs, output, error) = RunPlan(before);

        Assert.Equal(1, exitCode);
        Assert.Contains("vNext", output + error, StringComparison.Ordinal);
        Assert.False(outputs.ContainsKey("release"));
    }

    // The way out of the failing state: after the tag is fixed or deleted the next push plans normally.
    [Fact]
    public void After_the_malformed_tag_is_deleted_the_next_push_plans_normally()
    {
        var before = InitialCodeCommit();
        Tag("vNext");
        var docs = Commit("docs: fix a typo (#11)", null, ("docs/a.md", "changed"));
        Assert.Equal(1, RunPlan(before).ExitCode);

        Git("tag", "-d", "vNext");
        var (exitCode, outputs, _, error) = RunPlan(before);

        Assert.True(exitCode == 0, error);
        Assert.Equal("false", outputs["release"]);
        Assert.Equal(docs, outputs["sha"]);
    }
}
