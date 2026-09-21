using PodcastGenerator.Application.Runtime;
using PodcastGenerator.Cli;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Cli;

public class SetConfigCommandLineTests
{
    [Fact]
    public void Set_config_with_a_key_and_a_value_is_recognised()
    {
        var command = Assert.IsType<SetConfigCommand>(CommandLine.Parse(["--set-config", "OPENROUTER_API_KEY", "abc"]));

        Assert.Equal("OPENROUTER_API_KEY", command.Key);
        Assert.Equal("abc", command.Value);
    }

    [Fact]
    public void An_empty_key_or_value_still_parses_so_that_it_is_rejected_as_invalid_input_and_not_as_usage()
    {
        var command = Assert.IsType<SetConfigCommand>(CommandLine.Parse(["--set-config", "", "  "]));

        Assert.Equal(string.Empty, command.Key);
        Assert.Equal("  ", command.Value);
    }

    [Fact]
    public void A_value_that_starts_with_a_dash_is_a_value_and_not_an_unknown_option()
    {
        var command = Assert.IsType<SetConfigCommand>(CommandLine.Parse(["--set-config", "KEY", "-abc"]));

        Assert.Equal("-abc", command.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Set_config_with_the_wrong_number_of_arguments_is_a_usage_error(int count)
    {
        var args = new[] { "--set-config", "KEY", "value", "extra" }.Take(count).ToArray();

        var error = Assert.IsType<UsageError>(CommandLine.Parse(args));

        Assert.Contains("--set-config", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_config_after_another_argument_is_a_usage_error()
    {
        var error = Assert.IsType<UsageError>(CommandLine.Parse(["script.txt", "--set-config", "KEY", "value"]));

        Assert.Contains("first argument", error.Message, StringComparison.Ordinal);
    }

    // cli-set-config AC-12: help and version are still recognised first, so they never reach the config check.
    [Fact]
    public void Help_and_version_still_win_over_set_config()
    {
        Assert.IsType<HelpCommand>(CommandLine.Parse(["--set-config", "KEY", "--help"]));
        Assert.IsType<VersionCommand>(CommandLine.Parse(["--version", "--set-config", "KEY", "value"]));
    }

    [Fact]
    public void The_usage_text_shows_the_set_config_command()
    {
        Assert.Contains("--set-config <KEY> <VALUE>", CommandLine.Usage, StringComparison.Ordinal);
    }

    // cli-set-config AC-13: the value stays out of anything that prints the command.
    [Fact]
    public void The_command_text_does_not_contain_the_value()
    {
        var command = new SetConfigCommand("KEY", TestKeys.Sentinel);

        Assert.DoesNotContain(TestKeys.Sentinel, command.ToString(), StringComparison.Ordinal);
    }
}

public class SetConfigCliTests
{
    private const string KeyName = "OPENROUTER_API_KEY";

    private static async Task<(int Code, string Out, string Error)> SetAsync(ServiceFixture fixture, string key, string value)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await fixture.CreateRunner().SetConfigAsync(new SetConfigCommand(key, value), stdout, stderr, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static async Task<(int Code, string Out, string Error)> RunAsync(ServiceFixture fixture)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await fixture.CreateRunner().RunAsync(new RunCommand(fixture.ScriptPath, null), stdout, stderr, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // cli-set-config AC-5, AC-13
    [Fact]
    public async Task Set_config_saves_the_value_exits_0_makes_no_podcast_and_does_not_print_the_value()
    {
        var fixture = new ServiceFixture(withKeyFile: false);

        var (code, output, error) = await SetAsync(fixture, KeyName, TestKeys.Sentinel);

        Assert.Equal(0, code);
        Assert.Equal("Saved OPENROUTER_API_KEY." + Environment.NewLine, output);
        Assert.Empty(error);
        Assert.DoesNotContain(TestKeys.Sentinel, output + error, StringComparison.Ordinal);
        Assert.Equal(TestKeys.Sentinel, EnvFileParser.GetValue(fixture.FileSystem.Files[fixture.Paths.KeyFilePath], KeyName));
        Assert.Empty(fixture.Speech.Requests);
        Assert.Null(fixture.Workspaces.Last);
        Assert.Empty(fixture.Prompter.Asked);
    }

    // cli-set-config AC-4: exactly "Invalid input", no prefix, exit 1, file unchanged.
    [Theory]
    [InlineData("", "value")]
    [InlineData("MY KEY", "value")]
    [InlineData("KEY", "")]
    [InlineData("KEY", "   ")]
    public async Task Set_config_with_an_unusable_key_or_value_prints_exactly_Invalid_input_and_exits_1(string key, string value)
    {
        var fixture = new ServiceFixture(withKeyFile: false);
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = "OTHER=keep\n";

        var (code, output, error) = await SetAsync(fixture, key, value);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Equal("Invalid input" + Environment.NewLine, error);
        Assert.Equal("OTHER=keep\n", fixture.FileSystem.Files[fixture.Paths.KeyFilePath]);
    }

    [Fact]
    public async Task A_rejected_value_is_not_printed_back()
    {
        var fixture = new ServiceFixture(withKeyFile: false);

        var (_, output, error) = await SetAsync(fixture, "MY KEY", TestKeys.Sentinel);

        Assert.DoesNotContain(TestKeys.Sentinel, output + error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_failure_exits_1_with_the_file_named_and_the_value_not_printed()
    {
        var fixture = new ServiceFixture(withKeyFile: false);
        fixture.FileSystem.ReplaceFailure = new IOException($"boom {TestKeys.Sentinel}");

        var (code, output, error) = await SetAsync(fixture, KeyName, TestKeys.Sentinel);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.StartsWith("Error:", error, StringComparison.Ordinal);
        Assert.Contains(fixture.Paths.KeyFilePath, error, StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.Sentinel, error, StringComparison.Ordinal);
    }

    // cli-set-config AC-6: the config check comes before any other work, so a missing script is not reported until the key exists.
    [Fact]
    public async Task The_config_check_runs_before_the_script_is_looked_at()
    {
        var fixture = new ServiceFixture(withKeyFile: false);
        fixture.FileSystem.Files.Remove(fixture.ScriptPath);

        var (code, output, error) = await RunAsync(fixture);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Equal([KeyName], fixture.Prompter.Asked);
        Assert.Contains("--set-config", error, StringComparison.Ordinal);
        Assert.DoesNotContain("does not exist", error, StringComparison.Ordinal);
        Assert.True(fixture.FileSystem.FileExists(fixture.Paths.KeyFilePath));
    }

    // cli-set-config AC-7, AC-11
    [Fact]
    public async Task With_the_key_in_the_file_the_run_goes_ahead_without_a_prompt()
    {
        var fixture = new ServiceFixture();

        var (code, output, _) = await RunAsync(fixture);

        Assert.Equal(0, code);
        Assert.Equal(fixture.DefaultOutputPath + Environment.NewLine, output);
        Assert.Empty(fixture.Prompter.Asked);
        Assert.Single(fixture.Speech.Requests);
    }

    [Fact]
    public async Task With_the_key_only_in_the_environment_the_run_goes_ahead_without_a_prompt()
    {
        var fixture = new ServiceFixture(withKeyFile: false);
        fixture.Environment.Values[KeyName] = TestKeys.Sentinel;

        var (code, _, _) = await RunAsync(fixture);

        Assert.Equal(0, code);
        Assert.Empty(fixture.Prompter.Asked);
        Assert.Equal(TestKeys.Sentinel, Assert.Single(fixture.Speech.Keys));
    }

    // cli-set-config AC-8, AC-10, AC-11, AC-13
    [Fact]
    public async Task A_key_typed_at_the_prompt_is_saved_used_for_the_run_and_never_printed()
    {
        var fixture = new ServiceFixture(withKeyFile: false);
        fixture.Prompter.Answers.Add(TestKeys.Sentinel);

        var (code, output, error) = await RunAsync(fixture);

        Assert.Equal(0, code);
        Assert.Equal(TestKeys.Sentinel, Assert.Single(fixture.Speech.Keys));
        Assert.Equal($"{KeyName}={TestKeys.Sentinel}" + Environment.NewLine, fixture.FileSystem.Files[fixture.Paths.KeyFilePath]);
        Assert.DoesNotContain(TestKeys.Sentinel, output + error, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Prompter.Messages, message => message.Contains(TestKeys.Sentinel, StringComparison.Ordinal));
    }

    // cli-set-config AC-9
    [Fact]
    public async Task Five_invalid_entries_exit_1_and_make_no_request()
    {
        var fixture = new ServiceFixture(withKeyFile: false);
        fixture.Prompter.Answers.AddRange(["", " ", "", " ", ""]);

        var (code, output, error) = await RunAsync(fixture);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.StartsWith("Error:", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Speech.Requests);
        Assert.Null(fixture.Workspaces.Last);
    }

    [Fact]
    public async Task No_console_input_exits_1_with_a_message_and_makes_no_request()
    {
        var fixture = new ServiceFixture(withKeyFile: false);

        var (code, output, error) = await RunAsync(fixture);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains($"--set-config {KeyName}", error, StringComparison.Ordinal);
        Assert.Empty(fixture.Speech.Requests);
    }
}
