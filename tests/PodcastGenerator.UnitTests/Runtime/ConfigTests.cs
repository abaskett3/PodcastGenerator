using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Runtime;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Runtime;

public class ConfigInputTests
{
    // cli-set-config AC-4
    [Theory]
    [InlineData("OPENROUTER_API_KEY")]
    [InlineData("a")]
    [InlineData("some.key-2")]
    public void A_key_without_whitespace_is_valid(string key)
    {
        Assert.True(ConfigInput.IsValidKey(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("MY KEY")]
    [InlineData(" MY_KEY")]
    [InlineData("MY_KEY ")]
    [InlineData("MY\tKEY")]
    [InlineData("MY\nKEY")]
    public void A_blank_empty_or_whitespace_containing_key_is_invalid(string? key)
    {
        Assert.False(ConfigInput.IsValidKey(key));
    }

    // A key that the file format could not read back is rejected too (see ConfigInput.IsValidKey).
    [Theory]
    [InlineData("A=B")]
    [InlineData("=B")]
    [InlineData("#KEY")]
    public void A_key_the_file_format_could_not_read_back_is_invalid(string key)
    {
        Assert.False(ConfigInput.IsValidKey(key));
    }

    [Theory]
    [InlineData("sk-abc")]
    [InlineData("a value with spaces")]
    [InlineData("  padded  ")]
    [InlineData("-starts-with-dash")]
    public void A_value_with_visible_text_is_valid(string value)
    {
        Assert.True(ConfigInput.IsValidValue(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t ")]
    [InlineData("\n")]
    public void An_empty_blank_or_whitespace_only_value_is_invalid(string? value)
    {
        Assert.False(ConfigInput.IsValidValue(value));
    }

    [Theory]
    [InlineData("one\ntwo")]
    [InlineData("one\rtwo")]
    [InlineData("one\r\ntwo")]
    public void A_value_with_a_line_break_is_invalid_because_it_would_write_a_second_line(string value)
    {
        Assert.False(ConfigInput.IsValidValue(value));
    }

    [Fact]
    public void The_message_is_the_fixed_text()
    {
        Assert.Equal("Invalid input", ConfigInput.InvalidInputMessage);
        Assert.Equal("Invalid input", new InvalidInputException().Message);
    }
}

public class EnvFileEditorTests
{
    // cli-set-config AC-3
    [Fact]
    public void An_existing_line_is_updated_in_place_and_every_other_line_is_unchanged()
    {
        const string Before = "# my config\nFIRST=1\nOPENROUTER_API_KEY=old\n\nLAST=3\n";

        var after = EnvFileEditor.SetValue(Before, "OPENROUTER_API_KEY", "new");

        Assert.Equal("# my config\nFIRST=1\nOPENROUTER_API_KEY=new\n\nLAST=3\n", after);
    }

    [Fact]
    public void The_key_is_matched_without_regard_to_case_and_written_as_given()
    {
        var after = EnvFileEditor.SetValue("openrouter_api_key=old\nOTHER=x\n", "OPENROUTER_API_KEY", "new");

        Assert.Equal("OPENROUTER_API_KEY=new\nOTHER=x\n", after);
        // The app reads the key with an exact-case match, so the new value is the one it finds.
        Assert.Equal("new", EnvFileParser.GetValue(after, "OPENROUTER_API_KEY"));
    }

    [Fact]
    public void A_line_with_spaces_and_quotes_around_the_key_and_value_is_recognised()
    {
        var after = EnvFileEditor.SetValue("  KEY = \"old\"  \nOTHER=x\n", "KEY", "new");

        Assert.Equal("KEY=new\nOTHER=x\n", after);
    }

    [Fact]
    public void Every_duplicate_line_for_the_key_is_updated_so_the_last_one_cannot_hide_the_new_value()
    {
        var after = EnvFileEditor.SetValue("KEY=one\nOTHER=x\nKEY=two\n", "KEY", "new");

        Assert.Equal("KEY=new\nOTHER=x\nKEY=new\n", after);
        Assert.Equal("new", EnvFileParser.GetValue(after, "KEY"));
    }

    [Fact]
    public void A_comment_that_mentions_the_key_is_not_a_line_for_it()
    {
        var after = EnvFileEditor.SetValue("# KEY=old\nOTHER=x\n", "KEY", "new");

        Assert.Equal("# KEY=old\nOTHER=x\nKEY=new\n", after);
    }

    [Fact]
    public void A_key_that_only_starts_with_the_same_text_is_not_a_line_for_it()
    {
        var after = EnvFileEditor.SetValue("KEY_TWO=x\n", "KEY", "new");

        Assert.Equal("KEY_TWO=x\nKEY=new\n", after);
    }

    [Fact]
    public void A_missing_key_is_added_as_a_new_last_line()
    {
        var after = EnvFileEditor.SetValue("OTHER=x\n", "KEY", "new");

        Assert.Equal("OTHER=x\nKEY=new\n", after);
    }

    [Fact]
    public void A_last_line_without_a_line_ending_gets_one_before_the_new_line()
    {
        // The file's own ending is used when it has one; a single line with no ending at all gets the platform's.
        Assert.Equal("A=1\nB=2\nKEY=new\n", EnvFileEditor.SetValue("A=1\nB=2", "KEY", "new"));
        Assert.Equal("A=1\r\nB=2\r\nKEY=new\r\n", EnvFileEditor.SetValue("A=1\r\nB=2", "KEY", "new"));
        Assert.Equal(
            "OTHER=x" + Environment.NewLine + "KEY=new" + Environment.NewLine,
            EnvFileEditor.SetValue("OTHER=x", "KEY", "new"));
    }

    [Fact]
    public void An_empty_file_gets_one_line_with_the_platform_line_ending()
    {
        var after = EnvFileEditor.SetValue(string.Empty, "KEY", "new");

        Assert.Equal("KEY=new" + Environment.NewLine, after);
    }

    [Fact]
    public void Windows_line_endings_are_kept_and_used_for_a_new_line()
    {
        var updated = EnvFileEditor.SetValue("A=1\r\nKEY=old\r\nB=2\r\n", "KEY", "new");
        var added = EnvFileEditor.SetValue("A=1\r\nB=2\r\n", "KEY", "new");

        Assert.Equal("A=1\r\nKEY=new\r\nB=2\r\n", updated);
        Assert.Equal("A=1\r\nB=2\r\nKEY=new\r\n", added);
    }

    [Fact]
    public void A_lone_carriage_return_is_a_line_ending_like_the_parser_treats_it()
    {
        var after = EnvFileEditor.SetValue("A=1\rKEY=old\rB=2\r", "KEY", "new");

        Assert.Equal("A=1\rKEY=new\rB=2\r", after);
    }

    [Fact]
    public void A_byte_order_mark_is_dropped_and_the_first_line_is_still_recognised()
    {
        var after = EnvFileEditor.SetValue("﻿KEY=old\nOTHER=x\n", "KEY", "new");

        Assert.Equal("KEY=new\nOTHER=x\n", after);
    }

    [Fact]
    public void A_value_with_spaces_and_equals_signs_is_written_as_is_and_read_back_whole()
    {
        var after = EnvFileEditor.SetValue(string.Empty, "KEY", "a b=c");

        Assert.Equal("a b=c", EnvFileParser.GetValue(after, "KEY"));
    }

    // cli-set-config AC-10
    [Fact]
    public void Append_adds_a_new_last_line_and_leaves_an_existing_line_for_the_key_alone()
    {
        const string Before = "KEY=\nOTHER=x\n";

        var after = EnvFileEditor.AppendValue(Before, "KEY", "new");

        Assert.Equal("KEY=\nOTHER=x\nKEY=new\n", after);
        Assert.Equal("new", EnvFileParser.GetValue(after, "KEY")); // the last duplicate wins
    }

    [Fact]
    public void Append_to_a_file_without_a_final_line_ending_adds_one_first()
    {
        Assert.Equal("A=1\nB=2\nKEY=new\n", EnvFileEditor.AppendValue("A=1\nB=2", "KEY", "new"));
        Assert.Equal(
            "A=1" + Environment.NewLine + "KEY=new" + Environment.NewLine,
            EnvFileEditor.AppendValue("A=1", "KEY", "new"));
    }

    [Fact]
    public void Append_to_an_empty_file_writes_one_line()
    {
        Assert.Equal("KEY=new" + Environment.NewLine, EnvFileEditor.AppendValue(string.Empty, "KEY", "new"));
    }
}

public class ConfigServiceTests
{
    private const string KeyName = "OPENROUTER_API_KEY";
    private static readonly string Secret = TestKeys.Sentinel;

    private static ServiceFixture Fresh() => new(withKeyFile: false);

    private static string FileText(ServiceFixture fixture) => fixture.FileSystem.Files[fixture.Paths.KeyFilePath];

    // cli-set-config AC-1
    [Fact]
    public async Task Set_creates_the_config_folder_when_it_is_missing()
    {
        var fixture = Fresh();
        Assert.DoesNotContain(fixture.Paths.RuntimeFolder, fixture.FileSystem.Directories);

        await fixture.CreateConfigService().SetAsync(KeyName, "value", CancellationToken.None);

        Assert.Contains(fixture.Paths.RuntimeFolder, fixture.FileSystem.Directories);
    }

    // cli-set-config AC-2
    [Fact]
    public async Task Set_creates_the_file_when_it_is_missing_and_writes_the_line()
    {
        var fixture = Fresh();

        await fixture.CreateConfigService().SetAsync(KeyName, "value", CancellationToken.None);

        Assert.Equal($"{KeyName}=value" + Environment.NewLine, FileText(fixture));
    }

    [Fact]
    public async Task The_key_file_is_written_as_a_private_file()
    {
        var fixture = Fresh();

        await fixture.CreateConfigService().SetAsync(KeyName, "value", CancellationToken.None);

        Assert.Contains(fixture.Paths.KeyFilePath, fixture.FileSystem.PrivateFiles);
    }

    // cli-set-config AC-3
    [Fact]
    public async Task Set_updates_the_existing_line_in_place_matching_the_key_without_regard_to_case()
    {
        var fixture = Fresh();
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = "# note\nopenrouter_api_key=old\nOTHER=keep\n";

        await fixture.CreateConfigService().SetAsync(KeyName, "new", CancellationToken.None);

        Assert.Equal($"# note\n{KeyName}=new\nOTHER=keep\n", FileText(fixture));
    }

    [Fact]
    public async Task Set_adds_a_new_line_when_the_key_is_not_in_the_file()
    {
        var fixture = Fresh();
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = "OTHER=keep\n";

        await fixture.CreateConfigService().SetAsync("NEW_KEY", "value", CancellationToken.None);

        Assert.Equal("OTHER=keep\nNEW_KEY=value\n", FileText(fixture));
    }

    [Fact]
    public async Task Set_accepts_any_key_name_not_only_the_required_ones()
    {
        var fixture = Fresh();

        await fixture.CreateConfigService().SetAsync("SOME_FUTURE_SETTING", "on", CancellationToken.None);

        Assert.Equal("on", EnvFileParser.GetValue(FileText(fixture), "SOME_FUTURE_SETTING"));
    }

    [Fact]
    public async Task Set_trims_the_value_so_the_file_holds_what_the_parser_would_read()
    {
        var fixture = Fresh();

        await fixture.CreateConfigService().SetAsync(KeyName, "  spaced  ", CancellationToken.None);

        Assert.Equal($"{KeyName}=spaced" + Environment.NewLine, FileText(fixture));
    }

    // cli-set-config AC-4
    [Theory]
    [InlineData("", "value")]
    [InlineData("   ", "value")]
    [InlineData("MY KEY", "value")]
    [InlineData("MY\tKEY", "value")]
    [InlineData("KEY", "")]
    [InlineData("KEY", "   ")]
    [InlineData("KEY", "line one\nline two")]
    public async Task Set_rejects_an_unusable_key_or_value_with_the_fixed_message_and_changes_nothing(string key, string value)
    {
        var fixture = Fresh();
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = "OTHER=keep\n";

        var exception = await Assert.ThrowsAsync<InvalidInputException>(() => fixture.CreateConfigService().SetAsync(key, value, CancellationToken.None));

        Assert.Equal("Invalid input", exception.Message);
        Assert.Equal("OTHER=keep\n", FileText(fixture));
    }

    [Fact]
    public async Task Set_with_an_unusable_input_does_not_even_create_the_folder_or_file()
    {
        var fixture = Fresh();

        await Assert.ThrowsAsync<InvalidInputException>(() => fixture.CreateConfigService().SetAsync("MY KEY", "value", CancellationToken.None));

        Assert.DoesNotContain(fixture.Paths.RuntimeFolder, fixture.FileSystem.Directories);
        Assert.False(fixture.FileSystem.FileExists(fixture.Paths.KeyFilePath));
    }

    [Fact]
    public async Task A_write_failure_is_reported_with_the_file_and_never_the_value()
    {
        var fixture = Fresh();
        fixture.FileSystem.ReplaceFailure = new IOException($"disk full while writing {Secret}");

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.CreateConfigService().SetAsync(KeyName, Secret, CancellationToken.None));

        Assert.Contains(fixture.Paths.KeyFilePath, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    // cli-set-config AC-6
    [Fact]
    public async Task Ensure_creates_the_folder_and_an_empty_file_when_they_are_missing()
    {
        var fixture = Fresh();
        fixture.Prompter.Answers.Add("value");

        await fixture.CreateConfigService(new RequiredConfig([])).EnsureRequiredAsync(CancellationToken.None);

        Assert.Contains(fixture.Paths.RuntimeFolder, fixture.FileSystem.Directories);
        Assert.Equal(string.Empty, FileText(fixture));
        Assert.Empty(fixture.Prompter.Asked);
    }

    [Fact]
    public async Task Ensure_leaves_an_existing_file_alone()
    {
        var fixture = new ServiceFixture(); // has a key file
        var before = FileText(fixture);

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal(before, FileText(fixture));
        Assert.Empty(fixture.Prompter.Asked);
        Assert.Empty(fixture.Prompter.Messages);
    }

    // cli-set-config AC-7
    [Fact]
    public async Task A_value_only_in_the_file_does_not_trigger_a_prompt()
    {
        var fixture = Fresh();
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = $"{KeyName}=from-file\n";

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Empty(fixture.Prompter.Asked);
    }

    [Fact]
    public async Task A_value_only_in_the_environment_does_not_trigger_a_prompt_and_leaves_the_file_unchanged()
    {
        var fixture = Fresh();
        fixture.Environment.Values[KeyName] = "from-env";

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Empty(fixture.Prompter.Asked);
        Assert.Equal(string.Empty, FileText(fixture));
    }

    [Fact]
    public async Task A_value_in_both_places_does_not_trigger_a_prompt()
    {
        var fixture = Fresh();
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = $"{KeyName}=from-file\n";
        fixture.Environment.Values[KeyName] = "from-env";

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Empty(fixture.Prompter.Asked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_environment_variable_and_an_empty_file_value_count_as_missing(string environmentValue)
    {
        var fixture = Fresh();
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = $"{KeyName}=\n";
        fixture.Environment.Values[KeyName] = environmentValue;
        fixture.Prompter.Answers.Add("typed");

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal([KeyName], fixture.Prompter.Asked);
    }

    // cli-set-config AC-8
    [Fact]
    public async Task A_missing_value_prints_the_fixed_message_with_the_key_name_and_asks_for_it()
    {
        var fixture = Fresh();
        fixture.Prompter.Answers.Add("typed");

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal(["OPENROUTER_API_KEY not found. Please set this config value to continue."], fixture.Prompter.Messages);
        Assert.Equal([KeyName], fixture.Prompter.Asked);
    }

    // cli-set-config AC-10
    [Fact]
    public async Task An_accepted_value_is_appended_and_no_existing_line_is_changed()
    {
        var fixture = Fresh();
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = $"# mine\n{KeyName}=\nOTHER=keep\n";
        fixture.Prompter.Answers.Add("typed");

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal($"# mine\n{KeyName}=\nOTHER=keep\n{KeyName}=typed\n", FileText(fixture));
        Assert.Equal("typed", EnvFileParser.GetValue(FileText(fixture), KeyName));
    }

    // cli-set-config AC-9
    [Fact]
    public async Task An_invalid_entry_prints_the_fixed_message_and_asks_again()
    {
        var fixture = Fresh();
        fixture.Prompter.Answers.AddRange(["", "   ", "good"]);

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal(3, fixture.Prompter.Asked.Count);
        Assert.Equal(
            ["OPENROUTER_API_KEY not found. Please set this config value to continue.", "Invalid input", "Invalid input"],
            fixture.Prompter.Messages);
        Assert.Equal("good", EnvFileParser.GetValue(FileText(fixture), KeyName));
    }

    [Fact]
    public async Task The_fifth_attempt_can_still_succeed()
    {
        var fixture = Fresh();
        fixture.Prompter.Answers.AddRange(["", "", "", "", "fifth"]);

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal(5, fixture.Prompter.Asked.Count);
        Assert.Equal("fifth", EnvFileParser.GetValue(FileText(fixture), KeyName));
    }

    [Fact]
    public async Task Five_invalid_entries_fail_the_run_and_a_sixth_is_never_asked_for()
    {
        var fixture = Fresh();
        fixture.Prompter.Answers.AddRange(["", "  ", "", " ", "", "valid-but-too-late"]);

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None));

        Assert.Equal(5, fixture.Prompter.Asked.Count);
        Assert.Equal(5, fixture.Prompter.Messages.Count(message => message == "Invalid input"));
        Assert.Contains(KeyName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("5 attempts", exception.Message, StringComparison.Ordinal);
        Assert.Null(EnvFileParser.GetValue(FileText(fixture), KeyName));
    }

    [Fact]
    public void The_number_of_attempts_is_five()
    {
        Assert.Equal(5, ConfigService.MaxAttempts);
    }

    // The case the spec leaves open: no console input at all. Decision D-3 in docs/designs/cli-set-config.md.
    [Fact]
    public async Task No_console_input_fails_at_once_with_a_message_that_says_how_to_set_the_value()
    {
        var fixture = Fresh(); // the fake console has no answers left, like a closed standard input

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None));

        Assert.Single(fixture.Prompter.Asked);
        Assert.DoesNotContain("Invalid input", fixture.Prompter.Messages);
        Assert.Contains($"--set-config {KeyName}", exception.Message, StringComparison.Ordinal);
        Assert.Contains(fixture.Paths.KeyFilePath, exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{KeyName} environment variable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Input_that_ends_after_an_invalid_entry_stops_without_using_the_remaining_attempts()
    {
        var fixture = Fresh();
        fixture.Prompter.Answers.AddRange(["", null]);

        await Assert.ThrowsAsync<UserFacingException>(() => fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None));

        Assert.Equal(2, fixture.Prompter.Asked.Count);
    }

    // The list of required values is meant to grow.
    [Fact]
    public async Task Only_the_required_values_that_are_missing_are_asked_for_in_order()
    {
        var fixture = Fresh();
        fixture.FileSystem.Files[fixture.Paths.KeyFilePath] = "SECOND=present\n";
        fixture.Environment.Values["FOURTH"] = "present";
        fixture.Prompter.Answers.AddRange(["one", "three"]);

        await fixture.CreateConfigService(new RequiredConfig(["FIRST", "SECOND", "THIRD", "FOURTH"])).EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal(["FIRST", "THIRD"], fixture.Prompter.Asked);
        Assert.Equal("SECOND=present\nFIRST=one\nTHIRD=three\n", FileText(fixture));
    }

    // cli-set-config AC-13
    [Fact]
    public async Task A_value_is_never_in_a_message_shown_to_the_person_or_in_an_error()
    {
        var fixture = Fresh();
        fixture.Prompter.Answers.AddRange(["", Secret]);

        await fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None);

        Assert.DoesNotContain(fixture.Prompter.Messages, message => message.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failure_to_save_a_typed_value_is_reported_with_the_file_and_never_the_value()
    {
        var fixture = Fresh();
        fixture.FileSystem.ReplaceFailure = new UnauthorizedAccessException($"denied writing {Secret}");
        fixture.Prompter.Answers.Add(Secret);

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.CreateConfigService().EnsureRequiredAsync(CancellationToken.None));

        Assert.Contains(fixture.Paths.KeyFilePath, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>The config service over the real file system in a temporary folder that stands in for the user profile, so the folder
/// and file creation and the in-place update are checked on a real disk (never the real profile or key file).</summary>
public sealed class ConfigServiceOnDiskTests : IDisposable
{
    private readonly string _profile = Path.Combine(Path.GetTempPath(), "pg-config-" + Guid.NewGuid().ToString("N"));
    private readonly PodcastGenerator.Infrastructure.FileSystem.RuntimePaths _paths;
    private readonly FakeConfigPrompter _prompter = new();
    private readonly FakeEnvironmentVariables _environment = new();

    public ConfigServiceOnDiskTests()
    {
        Directory.CreateDirectory(_profile);
        _paths = new PodcastGenerator.Infrastructure.FileSystem.RuntimePaths(_profile);
    }

    public void Dispose() => Directory.Delete(_profile, recursive: true);

    private ConfigService Create() =>
        new(new PodcastGenerator.Infrastructure.FileSystem.PhysicalFileSystem(), _paths, _environment, _prompter, new RequiredConfig(["OPENROUTER_API_KEY"]));

    // cli-set-config AC-1, AC-2
    [Fact]
    public async Task Set_creates_the_folder_and_the_file_on_disk_and_writes_the_line()
    {
        Assert.False(Directory.Exists(_paths.RuntimeFolder));

        await Create().SetAsync("OPENROUTER_API_KEY", "value", CancellationToken.None);

        Assert.Equal(Path.Combine(_profile, ".config", "PodcastGenerator", "PodcastGenerator.env"), _paths.KeyFilePath);
        Assert.Equal("OPENROUTER_API_KEY=value" + Environment.NewLine, await File.ReadAllTextAsync(_paths.KeyFilePath));
    }

    // cli-set-config AC-3
    [Fact]
    public async Task Set_updates_the_line_in_place_on_disk_and_keeps_the_other_lines_and_their_line_endings()
    {
        Directory.CreateDirectory(_paths.RuntimeFolder);
        await File.WriteAllTextAsync(_paths.KeyFilePath, "# mine\r\nopenrouter_api_key=old\r\nOTHER=keep\r\n");

        await Create().SetAsync("OPENROUTER_API_KEY", "new", CancellationToken.None);

        Assert.Equal("# mine\r\nOPENROUTER_API_KEY=new\r\nOTHER=keep\r\n", await File.ReadAllTextAsync(_paths.KeyFilePath));
    }

    // cli-set-config AC-6, AC-8, AC-10
    [Fact]
    public async Task A_typed_value_is_appended_on_disk_after_the_folder_and_file_are_created()
    {
        _prompter.Answers.Add("typed");

        await Create().EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal("OPENROUTER_API_KEY=typed" + Environment.NewLine, await File.ReadAllTextAsync(_paths.KeyFilePath));
    }

    [Fact]
    public async Task A_key_file_that_already_has_the_value_is_not_rewritten()
    {
        Directory.CreateDirectory(_paths.RuntimeFolder);
        await File.WriteAllBytesAsync(_paths.KeyFilePath, [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes("OPENROUTER_API_KEY=abc\n")]);
        var before = await File.ReadAllBytesAsync(_paths.KeyFilePath);

        await Create().EnsureRequiredAsync(CancellationToken.None);

        Assert.Equal(before, await File.ReadAllBytesAsync(_paths.KeyFilePath));
        Assert.Empty(_prompter.Asked);
    }
}
