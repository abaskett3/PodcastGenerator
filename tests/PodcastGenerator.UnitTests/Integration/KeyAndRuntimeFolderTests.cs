using System.Net;
using System.Text;
using PodcastGenerator.Infrastructure;
using PodcastGenerator.Infrastructure.FileSystem;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>The API key and the runtime folder through the real file system in a temporary "user profile" (AC-22 to AC-27,
/// AC-43, AC-44). Every key here is a fake.</summary>
public sealed class KeyAndRuntimeFolderTests : IDisposable
{
    private const string FileKey = "sk-test-FILE-KEY-0001";
    private const string EnvironmentKey = "sk-test-ENV-KEY-0002";

    private readonly Pipeline _pipeline = new(withKeyFile: false);

    public void Dispose() => _pipeline.Dispose();

    private string OneLineScript() => _pipeline.WriteScript("CECIL: Good evening.\n");

    private string SentKey() => Assert.Single(_pipeline.Http.Requests).AuthorizationParameter!;

    // AC-22: the runtime folder is <profile>/.config/PodcastGenerator.
    [Fact]
    public void The_runtime_and_default_output_folders_come_from_the_user_profile_folder()
    {
        var paths = new RuntimePaths(_pipeline.Profile);

        Assert.Equal(Path.Combine(_pipeline.Profile, ".config", "PodcastGenerator"), paths.RuntimeFolder);
        Assert.Equal(Path.Combine(_pipeline.Profile, ".config", "PodcastGenerator", "PodcastGenerator.env"), paths.KeyFilePath);
        Assert.Equal(Path.Combine(_pipeline.Profile, "PodcastGenerator"), paths.DefaultOutputDirectory);
    }

    // AC-8, AC-22: the registered paths are those of the current user's profile folder (strings only; no folder is touched).
    [Fact]
    public void The_registered_paths_are_built_from_the_UserProfile_special_folder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = RuntimePaths.ForCurrentUser();

        Assert.Equal(Path.Combine(profile, ".config", "PodcastGenerator"), paths.RuntimeFolder);
        Assert.Equal(Path.Combine(profile, "PodcastGenerator"), paths.DefaultOutputDirectory);
    }

    // AC-25 through the real file reader: BOM, CRLF, comments, other keys, spaces and quotes.
    [Fact]
    public async Task A_key_file_with_a_byte_order_mark_CRLF_comments_spaces_quotes_and_other_keys_is_read()
    {
        var contents = "# the key file\r\nOTHER_KEY=not-this-one\r\n\r\n  OPENROUTER_API_KEY = \"" + FileKey + "\"  \r\n";
        Directory.CreateDirectory(_pipeline.Paths.RuntimeFolder);
        File.WriteAllBytes(_pipeline.Paths.KeyFilePath, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(contents)).ToArray());

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(FileKey, SentKey());
    }

    // User decision (cli-set-config fix round 1): the key name in the file is read without regard to case.
    [Fact]
    public async Task A_lowercase_key_name_in_the_key_file_is_used()
    {
        _pipeline.WriteKeyFile($"openrouter_api_key={FileKey}\n");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(FileKey, SentKey());
        Assert.Empty(_pipeline.Prompter.Asked);
    }

    [Fact]
    public async Task The_last_duplicate_in_the_key_file_wins()
    {
        _pipeline.WriteKeyFile($"OPENROUTER_API_KEY=first\nOPENROUTER_API_KEY={FileKey}\n");

        await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(FileKey, SentKey());
    }

    // AC-23: file wins over the environment variable.
    [Fact]
    public async Task The_key_file_wins_when_the_environment_variable_is_also_set()
    {
        _pipeline.WriteKeyFile($"OPENROUTER_API_KEY={FileKey}\n");
        _pipeline.Environment.Values["OPENROUTER_API_KEY"] = EnvironmentKey;

        await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(FileKey, SentKey());
    }

    [Fact]
    public async Task The_environment_variable_is_used_when_there_is_no_key_file()
    {
        _pipeline.Environment.Values["OPENROUTER_API_KEY"] = EnvironmentKey;

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(EnvironmentKey, SentKey());
    }

    [Theory]
    [InlineData("OTHER_KEY=x\n")]
    [InlineData("OPENROUTER_API_KEY=\n")]
    [InlineData("# OPENROUTER_API_KEY=commented\n")]
    public async Task The_environment_variable_is_used_when_the_key_file_has_no_usable_key(string fileContents)
    {
        _pipeline.WriteKeyFile(fileContents);
        _pipeline.Environment.Values["OPENROUTER_API_KEY"] = EnvironmentKey;

        await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(EnvironmentKey, SentKey());
    }

    // AC-24, D-13, and cli-set-config AC-8: no key and no console input to ask for it. The message names the file and how to set
    // the value (--set-config), no request is made, and the runtime folder and key file exist.
    [Fact]
    public async Task No_key_gives_the_full_key_file_path_and_the_line_format_makes_no_request_and_creates_the_runtime_folder()
    {
        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.Empty(_pipeline.Http.Requests);
        Assert.Empty(_pipeline.Out.ToString());
        var error = _pipeline.Err.ToString();
        Assert.Contains(_pipeline.Paths.KeyFilePath, error, StringComparison.Ordinal);
        Assert.Contains("--set-config OPENROUTER_API_KEY", error, StringComparison.Ordinal);
        // CLAUDE.md, Usage: the error says where the key file goes and what it must contain (the line KEY=<value>).
        Assert.Contains("OPENROUTER_API_KEY=<value>", error, StringComparison.Ordinal);
        Assert.Equal(["OPENROUTER_API_KEY"], _pipeline.Prompter.Asked);
        Assert.True(Directory.Exists(_pipeline.Paths.RuntimeFolder));
        Assert.True(File.Exists(_pipeline.Paths.KeyFilePath));
        Assert.False(File.Exists(_pipeline.DefaultOutputPath));
    }

    // AC-22, AC-43: the default style is created on the first run, never overwritten, and an edit takes effect next run.
    [Fact]
    public async Task The_default_style_is_created_on_the_first_run_is_never_overwritten_and_an_edit_changes_the_next_request()
    {
        _pipeline.Environment.Values["OPENROUTER_API_KEY"] = EnvironmentKey;
        Assert.False(Directory.Exists(_pipeline.Paths.RuntimeFolder));
        var script = OneLineScript();

        await _pipeline.RunAsync(script);

        Assert.True(File.Exists(_pipeline.Paths.StyleFilePath));
        var created = File.ReadAllText(_pipeline.Paths.StyleFilePath);
        Assert.Equal(new EmbeddedDefaultResources().DefaultStyle.ReplaceLineEndings("\n"), created.ReplaceLineEndings("\n"));
        Assert.Contains("{transcript}", created, StringComparison.Ordinal);
        var firstDirection = _pipeline.Http.Requests[0].Direction;
        Assert.Contains("Deadpan and warm", firstDirection, StringComparison.Ordinal);

        var edited = created.Replace("Deadpan and warm", "Brisk and bright EDIT-MARK", StringComparison.Ordinal);
        File.WriteAllText(_pipeline.Paths.StyleFilePath, edited);
        _pipeline.Http.Requests.Clear();
        await _pipeline.RunAsync(script);

        Assert.Equal(edited, File.ReadAllText(_pipeline.Paths.StyleFilePath));
        var secondRequest = Assert.Single(_pipeline.Http.Requests);
        Assert.Contains("EDIT-MARK", secondRequest.Direction, StringComparison.Ordinal);
        Assert.DoesNotContain("Deadpan and warm", secondRequest.Direction, StringComparison.Ordinal);
        Assert.Equal("Good evening.", secondRequest.Transcript);
    }

    // AC-44: an unusable style file is an error naming the file, saying that deleting it restores the default.
    [Fact]
    public async Task An_unusable_style_file_names_the_file_says_deleting_restores_the_default_and_makes_no_request()
    {
        _pipeline.Environment.Values["OPENROUTER_API_KEY"] = EnvironmentKey;
        Directory.CreateDirectory(_pipeline.Paths.RuntimeFolder);
        File.WriteAllText(_pipeline.Paths.StyleFilePath, "A style with no placeholder for the script.");
        var script = OneLineScript();

        var exitCode = await _pipeline.RunAsync(script);

        Assert.Equal(1, exitCode);
        Assert.Empty(_pipeline.Http.Requests);
        var error = _pipeline.Err.ToString();
        Assert.Contains(_pipeline.Paths.StyleFilePath, error, StringComparison.Ordinal);
        Assert.Contains("delet", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("A style with no placeholder for the script.", File.ReadAllText(_pipeline.Paths.StyleFilePath));

        // The advice works: after deleting the file the default is restored and the run succeeds.
        File.Delete(_pipeline.Paths.StyleFilePath);
        _pipeline.ResetOutput();
        var again = await _pipeline.RunAsync(script);

        Assert.Equal(0, again);
        Assert.Contains("Deadpan and warm", Assert.Single(_pipeline.Http.Requests).Direction, StringComparison.Ordinal);
    }

    // AC-26: the key value is in no output and no written file, on success and on failure. Verified with a sentinel.
    [Fact]
    public async Task The_key_is_in_no_output_and_no_written_file_after_a_successful_run()
    {
        _pipeline.WriteKeyFile($"OPENROUTER_API_KEY={TestKeys.Sentinel}\n");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Out.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.FilesContaining(TestKeys.Sentinel));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_server_that_echoes_the_key_in_its_error_never_leaks_it_to_any_output_or_file(HttpStatusCode status)
    {
        _pipeline.WriteKeyFile($"OPENROUTER_API_KEY={TestKeys.Sentinel}\n");
        _pipeline.Http.Responder = _ => CannedSpeechHandler.Error(status, $"Invalid credentials {TestKeys.Sentinel} (test text)");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Out.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.Empty(_pipeline.FilesContaining(TestKeys.Sentinel));
    }

    [Fact]
    public async Task The_key_never_appears_when_the_run_fails_before_the_request_because_of_the_style_file()
    {
        _pipeline.WriteKeyFile($"OPENROUTER_API_KEY={TestKeys.Sentinel}\n");
        File.WriteAllText(_pipeline.Paths.StyleFilePath, "no placeholder");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Err.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestKeys.Sentinel, _pipeline.Out.ToString(), StringComparison.Ordinal);
    }

    // AC-27: the key of a key-shaped file elsewhere in the profile is never read.
    [Fact]
    public async Task A_config_env_in_the_profile_and_a_claude_credentials_folder_are_never_read()
    {
        File.WriteAllText(Path.Combine(_pipeline.Profile, "config.env"), "OPENROUTER_API_KEY=sk-test-WRONG-config\n");
        Directory.CreateDirectory(Path.Combine(_pipeline.Profile, ".claude", "credentials"));
        File.WriteAllText(Path.Combine(_pipeline.Profile, ".claude", "credentials", "key.env"), "OPENROUTER_API_KEY=sk-test-WRONG-claude\n");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.Empty(_pipeline.Http.Requests);
    }
}
