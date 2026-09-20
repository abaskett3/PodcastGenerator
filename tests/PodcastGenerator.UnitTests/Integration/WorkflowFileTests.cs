using System.Text.RegularExpressions;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Reads the workflow files as text and checks the properties the spec asks for that can be seen without running
/// GitHub Actions (AC-60 to AC-66, AC-71 to AC-76). These do not prove the workflows run; they guard against a later edit
/// that removes a required property. The workflows themselves could not be run here.</summary>
public sealed class WorkflowFileTests
{
    private static string Read(string name) => File.ReadAllText(RepositoryFiles.Combine(".github", "workflows", name)).ReplaceLineEndings("\n");

    /// <summary>The file without comment lines, so a comment that explains a rule does not count as the rule.</summary>
    private static string Code(string name) =>
        string.Join('\n', Read(name).Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    // AC-60: starts on every pull request, never skipped by a workflow-level filter.
    [Fact]
    public void The_pull_request_workflow_triggers_on_pull_request_with_no_path_or_branch_filter()
    {
        var code = Code("pull-request.yml");

        Assert.Matches(@"(?m)^on:\n  pull_request:", code);
        Assert.DoesNotMatch(@"(?m)^\s+(paths|paths-ignore|branches|branches-ignore):", code);
    }

    // AC-61: separate Windows and Linux jobs whose check names are stable and stated in the README.
    [Fact]
    public void There_are_separate_windows_and_linux_jobs_and_the_readme_states_their_check_names()
    {
        var code = Code("pull-request.yml");
        var readme = File.ReadAllText(RepositoryFiles.Combine("README.md"));

        Assert.Contains("name: Build and test (${{ matrix.name }})", code, StringComparison.Ordinal);
        Assert.Contains("os: windows-latest", code, StringComparison.Ordinal);
        Assert.Contains("os: ubuntu-latest", code, StringComparison.Ordinal);
        Assert.Contains("name: Windows", code, StringComparison.Ordinal);
        Assert.Contains("name: Linux", code, StringComparison.Ordinal);
        foreach (var check in new[] { "Build and test (Windows)", "Build and test (Linux)", "PR title (conventional commit)" })
        {
            Assert.Contains(check, readme, StringComparison.Ordinal);
        }

        Assert.Contains("name: PR title (conventional commit)", Code("pull-request-title.yml"), StringComparison.Ordinal);
    }

    // AC-62, AC-63: build and test steps are conditional on the classification, and use the documented commands.
    [Fact]
    public void Build_and_test_steps_run_only_when_code_changed_and_use_warnaserror_and_dotnet_test()
    {
        var code = Code("pull-request.yml");

        Assert.Contains("classify-changes.sh", code, StringComparison.Ordinal);
        Assert.Contains("dotnet build -warnaserror", code, StringComparison.Ordinal);
        Assert.Contains("dotnet test", code, StringComparison.Ordinal);
        Assert.Contains("dotnet-version: 10.0.x", code, StringComparison.Ordinal);

        // Every step that builds, restores or tests carries the condition.
        foreach (var command in new[] { "run: dotnet restore", "run: dotnet build", "run: dotnet test" })
        {
            var index = code.IndexOf(command, StringComparison.Ordinal);
            Assert.True(index > 0, $"Missing step: {command}");
            var stepStart = code.LastIndexOf("- name:", index, StringComparison.Ordinal);
            Assert.Contains("steps.changes.outputs.code == 'true'", code[stepStart..index], StringComparison.Ordinal);
        }
    }

    // AC-64: the title check runs again when the title is edited.
    [Fact]
    public void The_title_check_runs_again_when_the_title_is_edited_and_passes_the_title_through_an_environment_variable()
    {
        var code = Code("pull-request-title.yml");

        Assert.Matches(@"types: \[[^\]]*\bedited\b[^\]]*\]", code);
        Assert.Contains("PR_TITLE: ${{ github.event.pull_request.title }}", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"run:.*\$\{\{\s*github\.event\.pull_request\.title", code);
    }

    // AC-65, AC-75: no secret, no OpenRouter.
    [Theory]
    [InlineData("pull-request.yml")]
    [InlineData("pull-request-title.yml")]
    [InlineData("release.yml")]
    public void No_workflow_uses_a_repository_secret_or_names_OpenRouter(string workflow)
    {
        var text = Read(workflow);

        Assert.DoesNotMatch(@"secrets\.", text);
        Assert.DoesNotContain("OPENROUTER", text, StringComparison.OrdinalIgnoreCase);
    }

    // AC-66: the release workflow runs on pushes to main only.
    [Fact]
    public void The_release_workflow_runs_only_on_a_push_to_main()
    {
        var code = Code("release.yml");

        Assert.Matches(@"(?m)^on:\n  push:\n    branches: \[main\]\n", code);
        Assert.DoesNotContain("pull_request", code, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_dispatch", code, StringComparison.Ordinal);
        Assert.DoesNotContain("schedule:", code, StringComparison.Ordinal);
    }

    // AC-71, AC-73: no commit, no branch push, no forced or deleted or moved tags.
    [Fact]
    public void The_release_workflow_never_commits_pushes_a_branch_or_moves_a_tag()
    {
        var code = Code("release.yml");

        Assert.DoesNotMatch(@"git\s+commit", code);
        Assert.DoesNotMatch(@"git\s+push[^\n]*(--force|-f\b|HEAD|main)", code);
        Assert.DoesNotMatch(@"git\s+tag\s+(-f|-d|--force|--delete)", code);
        Assert.DoesNotContain("--clobber", code, StringComparison.Ordinal);

        // The only push is the new tag, and the only delete is the tag this run created when the release failed.
        var pushes = Regex.Matches(code, @"git push[^\n]*").Select(match => match.Value).ToList();
        Assert.Contains(pushes, push => push.Contains("refs/tags/v${VERSION}", StringComparison.Ordinal));
        Assert.All(pushes, push => Assert.Contains("refs/tags/", push, StringComparison.Ordinal));
    }

    // AC-72: tests on both systems, then packages, then publish; publish depends on all of them.
    [Fact]
    public void The_release_tests_on_windows_and_linux_before_packaging_and_only_the_publish_job_can_write()
    {
        var code = Code("release.yml");

        Assert.Contains("name: Release test (${{ matrix.name }})", code, StringComparison.Ordinal);
        Assert.Matches(@"(?s)test:.*os: windows-latest.*os: ubuntu-latest.*package:", code);
        Assert.Contains("needs: [plan, test]", code, StringComparison.Ordinal);
        Assert.Contains("needs: [plan, package]", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"contents: write"));
        var writeIndex = code.IndexOf("contents: write", StringComparison.Ordinal);
        Assert.True(writeIndex > code.IndexOf("publish:\n    name: Publish release", StringComparison.Ordinal));
        Assert.Contains("dotnet build -warnaserror -p:Version=", code, StringComparison.Ordinal);
    }

    // AC-72: on any failure neither the tag nor the release exists. A cancelled run is not a failure, so the cleanup must name
    // it, and it may remove only a tag this run created and only when the release was not published.
    [Fact]
    public void The_tag_cleanup_also_runs_when_the_run_is_cancelled_and_removes_only_a_tag_this_run_created()
    {
        var code = Code("release.yml");
        var stepStart = code.IndexOf("- name: Remove the tag this run created", StringComparison.Ordinal);
        Assert.True(stepStart > 0, "The tag cleanup step is missing.");
        var step = code[stepStart..];
        var condition = Regex.Match(step, @"(?m)^\s*if:\s*(?<condition>.+)$").Groups["condition"].Value;

        Assert.Contains("failure()", condition, StringComparison.Ordinal);
        Assert.Contains("cancelled()", condition, StringComparison.Ordinal);
        Assert.Contains("steps.tag.outputs.created == 'true'", condition, StringComparison.Ordinal);
        Assert.Contains("steps.release.outcome != 'success'", condition, StringComparison.Ordinal);
        Assert.Contains("id: release", code, StringComparison.Ordinal);
        Assert.Contains("git push origin --delete \"refs/tags/v${VERSION}\"", step, StringComparison.Ordinal);
    }

    // AC-69: the tag check must run before the "only ignored files changed" exit, so a malformed tag fails every run.
    [Fact]
    public void The_plan_step_checks_the_tags_before_it_decides_that_only_ignored_files_changed()
    {
        var code = Code("release.yml");

        var compute = code.IndexOf("compute-release.sh", StringComparison.Ordinal);
        var classify = code.IndexOf("classify-changes.sh", StringComparison.Ordinal);

        Assert.True(compute > 0 && classify > 0);
        Assert.True(compute < classify, "compute-release.sh (which checks the tags) must run before classify-changes.sh.");
    }

    // AC-70, AC-74: the version reaches the build; assets have the documented names; notes and latest are set.
    [Fact]
    public void The_release_passes_the_version_to_the_build_and_publishes_the_two_named_assets_with_notes()
    {
        var code = Code("release.yml");

        Assert.Contains("-p:PublishSingleFile=true", code, StringComparison.Ordinal);
        Assert.Contains("--self-contained", code, StringComparison.Ordinal);
        Assert.Contains("-p:Version=\"$VERSION\"", code, StringComparison.Ordinal);
        Assert.Contains("rid: win-x64", code, StringComparison.Ordinal);
        Assert.Contains("rid: linux-x64", code, StringComparison.Ordinal);
        Assert.Contains("extension: zip", code, StringComparison.Ordinal);
        Assert.Contains("extension: tar.gz", code, StringComparison.Ordinal);
        Assert.Contains("PodcastGenerator-${VERSION}-${{ matrix.rid }}.${{ matrix.extension }}", code, StringComparison.Ordinal);
        Assert.Contains("--generate-notes", code, StringComparison.Ordinal);
        Assert.Contains("--latest", code, StringComparison.Ordinal);
        Assert.DoesNotContain("osx-", code, StringComparison.Ordinal);
        Assert.DoesNotContain("arm64", code, StringComparison.Ordinal);
    }

    // AC-17: the executable's --version is checked against the release version before anything is published.
    [Fact]
    public void The_packaged_executable_is_run_and_its_version_compared_with_the_release_version()
    {
        var code = Code("release.yml");

        Assert.Contains("--version", code, StringComparison.Ordinal);
        Assert.Contains("!= \"$VERSION\"", code, StringComparison.Ordinal);
    }

    // AC-76: the README lists the three settings.
    [Fact]
    public void The_readme_lists_the_github_settings_to_change()
    {
        var readme = File.ReadAllText(RepositoryFiles.Combine("README.md"));

        Assert.Contains("Required status checks", readme, StringComparison.Ordinal);
        Assert.Contains("Squash merging", readme, StringComparison.Ordinal);
        Assert.Contains("Pull request title and commit details", readme, StringComparison.Ordinal);
        Assert.Contains("Workflow permissions", readme, StringComparison.Ordinal);
    }
}
