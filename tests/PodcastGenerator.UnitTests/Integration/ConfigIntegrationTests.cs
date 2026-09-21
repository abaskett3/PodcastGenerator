using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Runtime;
using PodcastGenerator.Cli;
using PodcastGenerator.Infrastructure;
using PodcastGenerator.Infrastructure.FileSystem;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Integration tests for <c>--set-config</c> and the required-config prompt (docs/specs/cli-set-config.md, AC-1 to
/// AC-13): the real dependency injection wiring, the real file system in a temporary "user profile", the real
/// <see cref="ConsoleConfigPrompter"/> reading from a <see cref="StringReader"/> (standard input, scripted), and the real
/// parser and API key provider reading the file back. Only the outside world is replaced: HTTP (a canned handler), the
/// profile folder, the environment variables and the clock. No test here reaches the network, reads the user's key file or
/// needs an interactive console. Every key value is a fake. Expected values come from the spec, not from what the code does.</summary>
public sealed class ConfigIntegrationTests : IDisposable
{
    private const string KeyName = "OPENROUTER_API_KEY";
    private const string NotFoundMessage = "OPENROUTER_API_KEY not found. Please set this config value to continue.";
    private const string TypedKey = "sk-test-TYPED-KEY-0003";
    private const string EnvironmentKey = "sk-test-ENV-KEY-0002";

    private readonly Pipeline _pipeline = new(withKeyFile: false);

    public void Dispose() => _pipeline.Dispose();

    private string OneLineScript() => _pipeline.WriteScript("CECIL: Good evening.\n");

    private string SentKey() => Assert.Single(_pipeline.Http.Requests).AuthorizationParameter!;

    private string[] ErrLines() => _pipeline.Err.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private void TypeAtTheConsole(params string[] lines) =>
        _pipeline.ConfigPrompter = new ConsoleConfigPrompter(
            new StringReader(string.Concat(lines.Select(line => line + "\n"))),
            _pipeline.Err,
            hideTyping: false);

    private static string KeyValue(string contents, string key) =>
        EnvFileParser.GetValue(contents, key) ?? "(missing)";

    private void WriteKeyFileBytes(string contents)
    {
        Directory.CreateDirectory(_pipeline.Paths.RuntimeFolder);
        File.WriteAllBytes(_pipeline.Paths.KeyFilePath, new UTF8Encoding(false).GetBytes(contents));
    }

    // ---------------------------------------------------------------- --set-config: create, write, update (AC-1, 2, 3, 5)

    // AC-1, AC-2, AC-5, AC-13
    [Fact]
    public async Task Set_config_on_a_fresh_profile_creates_the_folder_and_the_file_writes_the_line_and_makes_no_podcast()
    {
        Assert.False(Directory.Exists(_pipeline.Paths.RuntimeFolder));

        var exitCode = await _pipeline.RunSetConfigAsync(KeyName, TestKeys.Sentinel);

        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(_pipeline.Paths.RuntimeFolder));
        Assert.Equal([$"{KeyName}={TestKeys.Sentinel}"], File.ReadAllLines(_pipeline.Paths.KeyFilePath));
        Assert.Empty(_pipeline.Http.Requests);
        Assert.False(Directory.Exists(_pipeline.DefaultOutputDirectory));
        Assert.Empty(_pipeline.Workspaces.Created);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Out.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.Err.ToString());
        Assert.Empty(_pipeline.FilesContaining(TestKeys.Sentinel));
    }

    // AC-2: the folder exists, the file does not.
    [Fact]
    public async Task Set_config_creates_the_file_when_only_the_folder_exists()
    {
        Directory.CreateDirectory(_pipeline.Paths.RuntimeFolder);
        Assert.False(File.Exists(_pipeline.Paths.KeyFilePath));

        var exitCode = await _pipeline.RunSetConfigAsync("MY_KEY", "my-value");

        Assert.Equal(0, exitCode);
        Assert.Equal(["MY_KEY=my-value"], File.ReadAllLines(_pipeline.Paths.KeyFilePath));
    }

    // AC-3: the key is matched without regard to case, the line is updated where it is, no duplicate is added, and every other
    // line (a comment, another key, a line with no final line ending) is left exactly as it was.
    [Fact]
    public async Task Set_config_updates_the_existing_line_in_place_and_leaves_every_other_line_unchanged()
    {
        WriteKeyFileBytes("# my keys\r\nOTHER_KEY=keep me\r\nopenrouter_api_key=old\r\nLAST_KEY=\"quoted\"");

        var exitCode = await _pipeline.RunSetConfigAsync(KeyName, "sk-test-NEW");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "# my keys\r\nOTHER_KEY=keep me\r\nOPENROUTER_API_KEY=sk-test-NEW\r\nLAST_KEY=\"quoted\"",
            File.ReadAllText(_pipeline.Paths.KeyFilePath));
        Assert.Single(File.ReadAllLines(_pipeline.Paths.KeyFilePath), line => line.StartsWith(KeyName, StringComparison.OrdinalIgnoreCase));
    }

    // AC-3: the same line, same case, and the line is not moved.
    [Fact]
    public async Task Set_config_updates_a_line_in_the_middle_of_the_file_without_moving_it()
    {
        WriteKeyFileBytes("A=1\nMY_KEY=old\nB=2\n");

        await _pipeline.RunSetConfigAsync("MY_KEY", "new");

        Assert.Equal("A=1\nMY_KEY=new\nB=2\n", File.ReadAllText(_pipeline.Paths.KeyFilePath));
    }

    // AC-3: no line for the key yet, so a new line is added and the existing lines are unchanged.
    [Fact]
    public async Task Set_config_adds_a_new_line_when_the_key_is_not_in_the_file_and_leaves_the_existing_lines_alone()
    {
        WriteKeyFileBytes("A=1\nB=2\n");

        var exitCode = await _pipeline.RunSetConfigAsync("MY_KEY", "my-value");

        Assert.Equal(0, exitCode);
        Assert.Equal(["A=1", "B=2", "MY_KEY=my-value"], File.ReadAllLines(_pipeline.Paths.KeyFilePath));
        Assert.StartsWith("A=1\nB=2\n", File.ReadAllText(_pipeline.Paths.KeyFilePath), StringComparison.Ordinal);
    }

    // AC-3 and the Constraints: a key file rewritten by --set-config is still read correctly by the real parser, for a file
    // with a byte order mark, comments and other keys (the parser's rules apply to every line that was not touched).
    [Fact]
    public async Task A_file_with_a_byte_order_mark_and_comments_is_still_parsed_after_set_config_updates_it()
    {
        Directory.CreateDirectory(_pipeline.Paths.RuntimeFolder);
        File.WriteAllBytes(
            _pipeline.Paths.KeyFilePath,
            new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("# keys\r\nOTHER=1\r\nOPENROUTER_API_KEY=old\r\n")).ToArray());

        await _pipeline.RunSetConfigAsync(KeyName, "sk-test-NEW");

        var contents = File.ReadAllText(_pipeline.Paths.KeyFilePath);
        Assert.Equal("sk-test-NEW", KeyValue(contents, KeyName));
        Assert.Equal("1", KeyValue(contents, "OTHER"));
        Assert.Contains("# keys", contents, StringComparison.Ordinal);
    }

    // AC-3, AC-11: the value written by --set-config is the one the real key provider then sends on the wire.
    [Fact]
    public async Task A_value_saved_with_set_config_is_used_by_the_next_run_without_a_prompt()
    {
        await _pipeline.RunSetConfigAsync(KeyName, TypedKey);
        _pipeline.ResetOutput();

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(TypedKey, SentKey());
        Assert.Empty(_pipeline.Prompter.Asked);
        Assert.DoesNotContain("not found", _pipeline.Err.ToString(), StringComparison.Ordinal);
    }

    // AC-3: an update replaces the key that an earlier run used.
    [Fact]
    public async Task A_second_set_config_replaces_the_first_value_and_the_next_run_sends_the_new_one()
    {
        await _pipeline.RunSetConfigAsync(KeyName, "sk-test-FIRST");
        await _pipeline.RunSetConfigAsync(KeyName, "sk-test-SECOND");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal("sk-test-SECOND", SentKey());
        Assert.Single(File.ReadAllLines(_pipeline.Paths.KeyFilePath));
    }

    // The value is saved so that the real parser reads it back whole, also with spaces, "=" and "#" inside it, and with the
    // surrounding spaces removed (the parser trims them on reading).
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("  padded  ", "padded")]
    [InlineData("two words", "two words")]
    [InlineData("a=b=c", "a=b=c")]
    [InlineData("abc#def", "abc#def")]
    public async Task A_saved_value_is_read_back_by_the_parser(string given, string expected)
    {
        var exitCode = await _pipeline.RunSetConfigAsync("MY_KEY", given);

        Assert.Equal(0, exitCode);
        Assert.Single(File.ReadAllLines(_pipeline.Paths.KeyFilePath));
        Assert.Equal(expected, KeyValue(File.ReadAllText(_pipeline.Paths.KeyFilePath), "MY_KEY"));
    }

    // AC-3: a key other than the required one is accepted (spec open question 2, assumed: any key name is accepted).
    [Fact]
    public async Task Set_config_accepts_a_key_the_app_does_not_require()
    {
        var exitCode = await _pipeline.RunSetConfigAsync("SOME_FUTURE_SETTING", "on");

        Assert.Equal(0, exitCode);
        Assert.Equal(["SOME_FUTURE_SETTING=on"], File.ReadAllLines(_pipeline.Paths.KeyFilePath));
    }

    // ---------------------------------------------------------------- --set-config: invalid input (AC-4, AC-13)

    public static TheoryData<string, string> UnusableInputFromTheSpec => new()
    {
        { string.Empty, "value" },
        { "   ", "value" },
        { "\t", "value" },
        { "MY KEY", "value" },
        { "MY\tKEY", "value" },
        { " MY_KEY", "value" },
        { "MY_KEY ", "value" },
        { "MY_KEY", string.Empty },
        { "MY_KEY", "   " },
        { "MY_KEY", "\t" },
        { "MY_KEY", " \t " },
    };

    // The extra rules the design adds (D-7): a key the file format could not read back, and a value that would write a second
    // line. They are rejected with the same message.
    public static TheoryData<string, string> UnusableInputFromTheDesign => new()
    {
        { "A=B", "value" },
        { "=", "value" },
        { "#KEY", "value" },
        { "MY_KEY", "line one\nINJECTED=1" },
        { "MY_KEY", "line one\r\nINJECTED=1" },
        { "MY_KEY", "line one\rINJECTED=1" },
    };

    // AC-4: on a fresh profile the exact message, the failure exit code and no file or folder at all.
    [Theory]
    [MemberData(nameof(UnusableInputFromTheSpec))]
    [MemberData(nameof(UnusableInputFromTheDesign))]
    public async Task Set_config_with_an_unusable_key_or_value_prints_exactly_Invalid_input_and_creates_nothing(string key, string value)
    {
        var exitCode = await _pipeline.RunSetConfigAsync(key, value);

        Assert.Equal(1, exitCode);
        Assert.Equal("Invalid input" + Environment.NewLine, _pipeline.Err.ToString());
        Assert.Empty(_pipeline.Out.ToString());
        Assert.Empty(Pipeline.Entries(_pipeline.Profile));
        Assert.Empty(_pipeline.Http.Requests);
    }

    // AC-4: with a key file already in place, its bytes are not changed at all.
    [Theory]
    [MemberData(nameof(UnusableInputFromTheSpec))]
    [MemberData(nameof(UnusableInputFromTheDesign))]
    public async Task Set_config_with_an_unusable_key_or_value_leaves_an_existing_file_byte_for_byte_unchanged(string key, string value)
    {
        WriteKeyFileBytes("# keep\r\nMY_KEY=old\r\nOTHER=1");
        var before = File.ReadAllBytes(_pipeline.Paths.KeyFilePath);

        var exitCode = await _pipeline.RunSetConfigAsync(key, value);

        Assert.Equal(1, exitCode);
        Assert.Equal("Invalid input" + Environment.NewLine, _pipeline.Err.ToString());
        Assert.Equal(before, File.ReadAllBytes(_pipeline.Paths.KeyFilePath));
        Assert.Equal(["PodcastGenerator.env"], Pipeline.Entries(_pipeline.Paths.RuntimeFolder));
    }

    // AC-13: a rejected value that is a secret is not printed back either.
    [Fact]
    public async Task A_rejected_secret_value_is_not_printed_back()
    {
        var exitCode = await _pipeline.RunSetConfigAsync("MY KEY", TestKeys.Sentinel);

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Out.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Err.ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- a file failure (CLAUDE.md: exit 1, the message says what went wrong)

    // The key file path is a directory, so it cannot be created or replaced. The failure is exit code 1 with an error on
    // standard error, and the value is not printed (AC-13).
    [Fact]
    public async Task Set_config_that_cannot_write_the_file_exits_1_with_an_error_and_does_not_print_the_value()
    {
        Directory.CreateDirectory(_pipeline.Paths.KeyFilePath);

        var exitCode = await _pipeline.RunSetConfigAsync(KeyName, TestKeys.Sentinel);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("Error:", _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.Out.ToString());
        Assert.Empty(_pipeline.FilesContaining(TestKeys.Sentinel));
    }

    [Fact]
    public async Task A_run_that_cannot_prepare_the_key_file_exits_1_with_an_error_and_makes_no_request()
    {
        Directory.CreateDirectory(_pipeline.Paths.KeyFilePath);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.StartsWith("Error:", _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.Http.Requests);
        Assert.Empty(_pipeline.Out.ToString());
    }

    // ---------------------------------------------------------------- the main command: create what is missing (AC-6)

    // AC-6, AC-7: the key comes only from the environment. The folder and an (empty) file are created and the file is not
    // otherwise touched; no prompt appears.
    [Fact]
    public async Task A_run_with_the_key_only_in_the_environment_creates_the_folder_and_an_empty_file_and_does_not_prompt()
    {
        _pipeline.Environment.Values[KeyName] = EnvironmentKey;
        Assert.False(Directory.Exists(_pipeline.Paths.RuntimeFolder));

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(_pipeline.Paths.KeyFilePath));
        Assert.Equal(string.Empty, File.ReadAllText(_pipeline.Paths.KeyFilePath));
        Assert.Equal(EnvironmentKey, SentKey());
        Assert.Empty(_pipeline.Prompter.Asked);
        Assert.DoesNotContain("not found", _pipeline.Err.ToString(), StringComparison.Ordinal);
    }

    // AC-6: the folder exists, the file does not.
    [Fact]
    public async Task A_run_creates_the_key_file_when_only_the_folder_exists()
    {
        Directory.CreateDirectory(_pipeline.Paths.RuntimeFolder);
        _pipeline.Environment.Values[KeyName] = EnvironmentKey;

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(_pipeline.Paths.KeyFilePath));
    }

    // AC-6: "before doing any other work". With no key and a script that does not exist, the person is asked for the key first;
    // the missing script is reported only afterwards, and the typed key is already saved.
    [Fact]
    public async Task The_config_check_and_prompt_come_before_the_script_is_looked_at()
    {
        var missing = Path.Combine(_pipeline.ScriptDirectory, "missing.txt");
        TypeAtTheConsole(TypedKey);

        var exitCode = await _pipeline.RunAsync(missing);

        Assert.Equal(1, exitCode);
        var lines = ErrLines();
        var promptIndex = Array.FindIndex(lines, line => line == NotFoundMessage);
        var scriptIndex = Array.FindIndex(lines, line => line.Contains(missing, StringComparison.Ordinal));
        Assert.True(promptIndex >= 0, "The not-found message was not printed.");
        Assert.True(scriptIndex > promptIndex, "The missing script must be reported after the config step.");
        Assert.Equal([$"{KeyName}={TypedKey}"], File.ReadAllLines(_pipeline.Paths.KeyFilePath));
        Assert.Empty(_pipeline.Http.Requests);
    }

    // AC-6 (the other half): when the key is present the same failure is reported with no prompt.
    [Fact]
    public async Task With_the_key_present_a_missing_script_is_reported_without_a_prompt()
    {
        _pipeline.Environment.Values[KeyName] = EnvironmentKey;
        var missing = Path.Combine(_pipeline.ScriptDirectory, "missing.txt");

        var exitCode = await _pipeline.RunAsync(missing);

        Assert.Equal(1, exitCode);
        Assert.Contains(missing, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("not found. Please set", _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(_pipeline.Paths.KeyFilePath));
    }

    // ---------------------------------------------------------------- the main command: present values (AC-7)

    // AC-7: in the file only, in the environment only, in both: no prompt in any case. Each run goes through to the API.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_value_in_the_file_or_the_environment_or_both_does_not_trigger_a_prompt(bool inFile, bool inEnvironment)
    {
        var fileContents = inFile ? $"# mine\r\n{KeyName}=sk-test-FILE-KEY-0001\r\n" : string.Empty;
        if (inFile)
        {
            WriteKeyFileBytes(fileContents);
        }

        if (inEnvironment)
        {
            _pipeline.Environment.Values[KeyName] = EnvironmentKey;
        }

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Empty(_pipeline.Prompter.Asked);
        Assert.Empty(_pipeline.Prompter.Messages);
        Assert.DoesNotContain("not found", _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Equal(inFile ? "sk-test-FILE-KEY-0001" : EnvironmentKey, SentKey());
        Assert.Equal(fileContents, File.ReadAllText(_pipeline.Paths.KeyFilePath));
    }

    // AC-7: an empty value does not count as present, in the file (CLAUDE.md: an empty value is missing).
    [Fact]
    public async Task An_empty_value_in_the_file_counts_as_missing_and_prompts()
    {
        WriteKeyFileBytes($"{KeyName}=\n");
        TypeAtTheConsole(TypedKey);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Contains(NotFoundMessage, ErrLines());
        Assert.Equal(TypedKey, SentKey());
    }

    // AC-7: an environment variable set to the empty string does not count as present.
    [Fact]
    public async Task An_empty_environment_variable_counts_as_missing_and_prompts()
    {
        _pipeline.Environment.Values[KeyName] = string.Empty;
        TypeAtTheConsole(TypedKey);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Contains(NotFoundMessage, ErrLines());
        Assert.Equal(TypedKey, SentKey());
    }

    // ---------------------------------------------------------------- the main command: the prompt (AC-8, 9, 10, 11, 13)

    // AC-8, AC-11: the exact message, the key name on the prompt, the run then goes ahead with the typed value, exactly as it
    // does when the key was already there (one audio file at the default path, its path on standard output).
    [Fact]
    public async Task A_missing_key_prints_the_fixed_message_asks_for_it_saves_it_and_the_run_continues()
    {
        TypeAtTheConsole(TypedKey);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        var error = _pipeline.Err.ToString();
        Assert.Contains(NotFoundMessage + Environment.NewLine, error, StringComparison.Ordinal);
        Assert.Contains($"{KeyName}: ", error, StringComparison.Ordinal);
        Assert.True(
            error.IndexOf(NotFoundMessage, StringComparison.Ordinal) < error.IndexOf($"{KeyName}: ", StringComparison.Ordinal),
            "The message must come before the prompt.");
        Assert.Equal([$"{KeyName}={TypedKey}"], File.ReadAllLines(_pipeline.Paths.KeyFilePath));
        Assert.Equal(TypedKey, SentKey());
        Assert.Equal(_pipeline.DefaultOutputPath + Environment.NewLine, _pipeline.Out.ToString());
        Assert.True(File.Exists(_pipeline.DefaultOutputPath));
    }

    // AC-13: what was typed is never written to standard output, standard error (the prompt writes to it), or any file other
    // than the key file. It is in the key file, which is where the spec says it goes.
    [Fact]
    public async Task A_typed_secret_is_only_in_the_key_file()
    {
        TypeAtTheConsole(TestKeys.Sentinel);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Out.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.FilesContaining(TestKeys.Sentinel));
        Assert.Contains(TestKeys.Sentinel, File.ReadAllText(_pipeline.Paths.KeyFilePath), StringComparison.Ordinal);
    }

    // AC-9: blank, empty and whitespace-only entries are rejected with the exact message and the person is asked again.
    [Fact]
    public async Task Blank_entries_are_rejected_with_Invalid_input_and_asked_again_until_a_valid_one()
    {
        TypeAtTheConsole(string.Empty, "   ", "\t", TypedKey);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(3, Regex.Matches(_pipeline.Err.ToString(), Regex.Escape("Invalid input")).Count);
        Assert.Equal(4, Regex.Matches(_pipeline.Err.ToString(), Regex.Escape($"{KeyName}: ")).Count);
        Assert.Equal([$"{KeyName}={TypedKey}"], File.ReadAllLines(_pipeline.Paths.KeyFilePath));
        Assert.Equal(TypedKey, SentKey());
    }

    // AC-9: the fifth attempt can still be the valid one.
    [Fact]
    public async Task The_fifth_attempt_can_still_succeed()
    {
        TypeAtTheConsole(string.Empty, " ", "  ", "\t", TypedKey);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(TypedKey, SentKey());
    }

    // AC-9: five invalid entries in a row: the run fails with exit code 1, no podcast, nothing saved, and the sixth line of
    // input is never read.
    [Fact]
    public async Task Five_invalid_entries_fail_the_run_with_exit_code_1_and_a_sixth_line_is_never_read()
    {
        var input = new StringReader("\n \n\t\n  \n\n" + TypedKey + "\n");
        _pipeline.ConfigPrompter = new ConsoleConfigPrompter(input, _pipeline.Err, hideTyping: false);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.Equal(5, Regex.Matches(_pipeline.Err.ToString(), Regex.Escape("Invalid input")).Count);
        Assert.Equal(5, Regex.Matches(_pipeline.Err.ToString(), Regex.Escape($"{KeyName}: ")).Count);
        Assert.Equal(TypedKey, input.ReadLine());
        Assert.Empty(_pipeline.Http.Requests);
        Assert.Empty(_pipeline.Out.ToString());
        Assert.False(File.Exists(_pipeline.DefaultOutputPath));
        Assert.Equal(string.Empty, File.ReadAllText(_pipeline.Paths.KeyFilePath));
        Assert.Contains(ErrLines(), line => line.StartsWith("Error:", StringComparison.Ordinal));
    }

    // AC-10: append only. An existing line for the key (here, one with an empty value, so the key counts as missing) and every
    // other line stay exactly as they were, and the new line is added after them. The real parser then reads the new value
    // (the last duplicate wins).
    [Fact]
    public async Task A_typed_value_is_appended_and_no_existing_line_is_changed_or_removed()
    {
        WriteKeyFileBytes("# note\r\nOPENROUTER_API_KEY=\r\nOTHER=1");
        TypeAtTheConsole(TypedKey);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["# note", "OPENROUTER_API_KEY=", "OTHER=1", $"OPENROUTER_API_KEY={TypedKey}"],
            File.ReadAllLines(_pipeline.Paths.KeyFilePath));
        Assert.StartsWith("# note\r\nOPENROUTER_API_KEY=\r\nOTHER=1", File.ReadAllText(_pipeline.Paths.KeyFilePath), StringComparison.Ordinal);
        Assert.Equal(TypedKey, KeyValue(File.ReadAllText(_pipeline.Paths.KeyFilePath), KeyName));
        Assert.Equal(TypedKey, SentKey());
    }

    // AC-10 on the second run: what was appended is now found, so the next run does not ask again.
    [Fact]
    public async Task A_key_typed_once_is_not_asked_for_again_on_the_next_run()
    {
        var script = OneLineScript();
        TypeAtTheConsole(TypedKey);
        await _pipeline.RunAsync(script);
        _pipeline.ResetOutput();
        _pipeline.Http.Requests.Clear();
        _pipeline.ConfigPrompter = new ConsoleConfigPrompter(new StringReader(string.Empty), _pipeline.Err, hideTyping: false);

        var exitCode = await _pipeline.RunAsync(script);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("not found", _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Equal(TypedKey, SentKey());
    }

    // The required list is a list: with a second required name the missing ones are asked for in order, each with its own
    // message, and each is appended (spec, Summary: "the required-value list ... meant to support more config values").
    [Fact]
    public async Task Every_missing_required_value_is_asked_for_in_order_and_appended_on_disk()
    {
        WriteKeyFileBytes("SECOND_VALUE=already-here\n");
        var input = new StringReader("first-typed\nthird-typed\n");
        var prompter = new ConsoleConfigPrompter(input, _pipeline.Err, hideTyping: false);
        var service = new ConfigService(
            new PhysicalFileSystem(),
            _pipeline.Paths,
            _pipeline.Environment,
            prompter,
            new RequiredConfig([KeyName, "SECOND_VALUE", "THIRD_VALUE"]));

        await service.EnsureRequiredAsync(CancellationToken.None);

        var error = _pipeline.Err.ToString();
        Assert.StartsWith(NotFoundMessage + Environment.NewLine, error, StringComparison.Ordinal);
        Assert.Contains("THIRD_VALUE not found. Please set this config value to continue.", error, StringComparison.Ordinal);
        Assert.True(
            error.IndexOf(NotFoundMessage, StringComparison.Ordinal) < error.IndexOf("THIRD_VALUE not found", StringComparison.Ordinal),
            "The values must be asked for in the order of the list.");
        Assert.DoesNotContain("SECOND_VALUE not found", error, StringComparison.Ordinal);
        Assert.Equal(
            ["SECOND_VALUE=already-here", $"{KeyName}=first-typed", "THIRD_VALUE=third-typed"],
            File.ReadAllLines(_pipeline.Paths.KeyFilePath));
    }

    // The registered list of required values is exactly the API key today.
    [Fact]
    public void The_registered_required_values_are_only_the_api_key()
    {
        var services = new ServiceCollection();
        services.AddPodcastGeneratorApplication();
        services.AddPodcastGeneratorInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.Equal([KeyName], provider.GetRequiredService<RequiredConfig>().Keys);
    }

    // ---------------------------------------------------------------- no console input (spec open question 3: unspecified)

    // The spec does not define this case. CLAUDE.md says every failure is exit code 1 with a message that says what went wrong,
    // and that a run must never hang, so: exit 1, an Error line on standard error, no request, no podcast, the key file made.
    [Fact]
    public async Task A_missing_key_with_no_console_input_fails_with_exit_code_1_and_a_message()
    {
        _pipeline.ConfigPrompter = new ConsoleConfigPrompter(new StringReader(string.Empty), _pipeline.Err, hideTyping: false);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.Contains(NotFoundMessage, ErrLines());
        Assert.Contains(ErrLines(), line => line.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Empty(_pipeline.Http.Requests);
        Assert.Empty(_pipeline.Out.ToString());
        Assert.False(File.Exists(_pipeline.DefaultOutputPath));
        Assert.True(File.Exists(_pipeline.Paths.KeyFilePath));

        // CLAUDE.md, Usage: a missing key gives "a helpful, descriptive error that says where the key file goes and what it
        // must contain". The message still names the file and the line it holds (this replaces the "OPENROUTER_API_KEY=<key>"
        // assertion in KeyAndRuntimeFolderTests, which the coding agent changed).
        Assert.Contains(_pipeline.Paths.KeyFilePath, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Contains($"{KeyName}=", _pipeline.Err.ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- regression: what the change could have broken

    // A key file that is fine is read exactly as before: no rewrite, no prompt, same bytes, request authorized with it.
    [Fact]
    public async Task A_run_with_a_good_key_file_does_not_rewrite_it()
    {
        const string contents = "﻿# keys\r\nOTHER=x\r\n  OPENROUTER_API_KEY = \"sk-test-FILE-KEY-0001\"  \r\n";
        Directory.CreateDirectory(_pipeline.Paths.RuntimeFolder);
        File.WriteAllBytes(_pipeline.Paths.KeyFilePath, new UTF8Encoding(false).GetBytes(contents));
        var before = File.ReadAllBytes(_pipeline.Paths.KeyFilePath);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal("sk-test-FILE-KEY-0001", SentKey());
        Assert.Equal(before, File.ReadAllBytes(_pipeline.Paths.KeyFilePath));
    }

    // A usage error (no script, too many arguments) is reported as one, and asks for nothing: it never reaches the runner.
    [Theory]
    [InlineData("a.txt", "b.mp3", "c")]
    [InlineData("--set-config", "ONLY_KEY")]
    [InlineData("--set-config", "A", "B", "C")]
    [InlineData("script.txt", "--set-config", "KEY")]
    [InlineData("--nonsense")]
    public void A_usage_error_is_never_parsed_as_something_that_runs_or_writes(params string[] args)
    {
        Assert.IsType<UsageError>(CommandLine.Parse(args));
    }

    [Fact]
    public void No_arguments_at_all_is_a_usage_error()
    {
        Assert.IsType<UsageError>(CommandLine.Parse([]));
    }

    // AC-12: --help and --version win over everything, including a --set-config on the same line, so neither reaches the
    // config step (Program handles both before it builds any service).
    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("--set-config", "KEY", "VALUE", "--help")]
    [InlineData("--set-config", "KEY", "VALUE", "--version")]
    [InlineData("script.txt", "--help")]
    public void Help_and_version_win_over_every_other_argument(params string[] args)
    {
        var command = CommandLine.Parse(args);

        Assert.True(command is HelpCommand or VersionCommand, $"Parsed as {command}");
    }
}
