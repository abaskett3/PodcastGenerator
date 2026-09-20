using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Narration;
using PodcastGenerator.Application.Runtime;
using PodcastGenerator.Infrastructure.FileSystem;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Runtime;

public class EnvFileParserTests
{
    private const string Key = "OPENROUTER_API_KEY";

    // AC-25
    [Fact]
    public void A_plain_line_gives_the_value()
    {
        Assert.Equal("abc123", EnvFileParser.GetValue("OPENROUTER_API_KEY=abc123", Key));
    }

    [Fact]
    public void A_byte_order_mark_is_tolerated()
    {
        Assert.Equal("abc123", EnvFileParser.GetValue("﻿OPENROUTER_API_KEY=abc123\n", Key));
    }

    [Theory]
    [InlineData("OPENROUTER_API_KEY=abc\r\nOTHER=1\r\n")]
    [InlineData("OTHER=1\nOPENROUTER_API_KEY=abc\n")]
    [InlineData("OTHER=1\rOPENROUTER_API_KEY=abc\r")]
    public void Either_line_ending_is_accepted(string contents)
    {
        Assert.Equal("abc", EnvFileParser.GetValue(contents, Key));
    }

    [Fact]
    public void Blank_lines_and_comment_lines_are_ignored()
    {
        const string contents = "\n# a comment\n   \n  # an indented comment\n# OPENROUTER_API_KEY=commented-out\nOPENROUTER_API_KEY=real\n";

        Assert.Equal("real", EnvFileParser.GetValue(contents, Key));
    }

    [Theory]
    [InlineData("  OPENROUTER_API_KEY  =  spaced  ", "spaced")]
    [InlineData("OPENROUTER_API_KEY=\"double quoted\"", "double quoted")]
    [InlineData("OPENROUTER_API_KEY='single quoted'", "single quoted")]
    [InlineData("OPENROUTER_API_KEY=\"  padded inside  \"", "padded inside")]
    [InlineData("OPENROUTER_API_KEY=\"unbalanced", "\"unbalanced")]
    [InlineData("OPENROUTER_API_KEY=\"mixed'", "\"mixed'")]
    [InlineData("OPENROUTER_API_KEY=a=b==", "a=b==")]
    public void Spaces_and_one_pair_of_matching_quotes_are_trimmed(string line, string expected)
    {
        Assert.Equal(expected, EnvFileParser.GetValue(line, Key));
    }

    [Fact]
    public void Other_keys_are_ignored()
    {
        Assert.Null(EnvFileParser.GetValue("OTHER_KEY=1\nOPENROUTER_API_KEY_2=nope\n", Key));
    }

    [Fact]
    public void The_key_name_is_case_sensitive()
    {
        Assert.Null(EnvFileParser.GetValue("openrouter_api_key=abc", Key));
    }

    [Fact]
    public void The_last_duplicate_wins()
    {
        Assert.Equal("second", EnvFileParser.GetValue("OPENROUTER_API_KEY=first\nOPENROUTER_API_KEY=second\n", Key));
    }

    [Theory]
    [InlineData("OPENROUTER_API_KEY=")]
    [InlineData("OPENROUTER_API_KEY=   ")]
    [InlineData("OPENROUTER_API_KEY=\"\"")]
    [InlineData("OPENROUTER_API_KEY=abc\nOPENROUTER_API_KEY=")]
    [InlineData("")]
    public void An_empty_value_is_treated_as_missing(string contents)
    {
        Assert.Null(EnvFileParser.GetValue(contents, Key));
    }
}

public class ApiKeyProviderTests
{
    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly FakeEnvironmentVariables _environment = new();
    private readonly RuntimePaths _paths = new(ServiceFixture.UserProfile);

    private ApiKeyProvider Create() => new(_fileSystem, _paths, _environment);

    // AC-23
    [Fact]
    public async Task The_key_file_in_the_runtime_folder_is_used()
    {
        _fileSystem.Files[_paths.KeyFilePath] = "OPENROUTER_API_KEY=from-file\n";

        var key = await Create().FindAsync(CancellationToken.None);

        Assert.Equal("from-file", key?.Reveal());
    }

    [Fact]
    public async Task The_environment_variable_is_used_when_there_is_no_file()
    {
        _environment.Values["OPENROUTER_API_KEY"] = "from-env";

        var key = await Create().FindAsync(CancellationToken.None);

        Assert.Equal("from-env", key?.Reveal());
    }

    [Fact]
    public async Task When_both_are_set_the_file_wins()
    {
        _fileSystem.Files[_paths.KeyFilePath] = "OPENROUTER_API_KEY=from-file\n";
        _environment.Values["OPENROUTER_API_KEY"] = "from-env";

        var key = await Create().FindAsync(CancellationToken.None);

        Assert.Equal("from-file", key?.Reveal());
    }

    [Fact]
    public async Task When_the_file_has_no_key_the_variable_is_used()
    {
        _fileSystem.Files[_paths.KeyFilePath] = "SOMETHING_ELSE=1\nOPENROUTER_API_KEY=\n";
        _environment.Values["OPENROUTER_API_KEY"] = "from-env";

        var key = await Create().FindAsync(CancellationToken.None);

        Assert.Equal("from-env", key?.Reveal());
    }

    [Fact]
    public async Task No_key_anywhere_gives_null()
    {
        Assert.Null(await Create().FindAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_variable_is_not_a_key()
    {
        _environment.Values["OPENROUTER_API_KEY"] = "   ";

        Assert.Null(await Create().FindAsync(CancellationToken.None));
    }

    // AC-27
    [Fact]
    public async Task Keys_in_config_env_or_the_claude_credentials_folder_are_never_read()
    {
        _fileSystem.Files[Path.Combine(ServiceFixture.UserProfile, "config.env")] = "OPENROUTER_API_KEY=wrong\n";
        _fileSystem.Files[Path.Combine(ServiceFixture.UserProfile, ".claude", "credentials", "key.env")] = "OPENROUTER_API_KEY=wrong\n";
        _fileSystem.Files[Path.Combine(Directory.GetCurrentDirectory(), "config.env")] = "OPENROUTER_API_KEY=wrong\n";

        Assert.Null(await Create().FindAsync(CancellationToken.None));
    }

    [Fact]
    public void The_key_file_is_PodcastGenerator_env_in_the_runtime_folder()
    {
        Assert.Equal(
            Path.Combine(ServiceFixture.UserProfile, ".config", "PodcastGenerator", "PodcastGenerator.env"),
            _paths.KeyFilePath);
    }

    // AC-26: the key never appears in what the app says about it.
    [Fact]
    public void A_key_prints_as_a_redaction_placeholder()
    {
        var key = new PodcastGenerator.Domain.Security.ApiKey(TestKeys.Sentinel);

        Assert.DoesNotContain(TestKeys.Sentinel, key.ToString());
        Assert.DoesNotContain(TestKeys.Sentinel, $"{key}");
        Assert.DoesNotContain(TestKeys.Sentinel, System.Text.Json.JsonSerializer.Serialize(new { key }));
    }
}

public class RuntimePathsTests
{
    // AC-8, AC-22
    [Fact]
    public void Paths_are_built_from_the_user_profile_with_Path_Combine()
    {
        var paths = new RuntimePaths(Path.Combine(Path.GetTempPath(), "someone"));
        var profile = Path.Combine(Path.GetTempPath(), "someone");

        Assert.Equal(Path.Combine(profile, ".config", "PodcastGenerator"), paths.RuntimeFolder);
        Assert.Equal(Path.Combine(profile, "PodcastGenerator"), paths.DefaultOutputDirectory);
        Assert.Equal(Path.Combine(paths.RuntimeFolder, "PodcastGenerator.env"), paths.KeyFilePath);
        Assert.Equal(Path.Combine(paths.RuntimeFolder, "style.md"), paths.StyleFilePath);
    }

    [Fact]
    public void For_current_user_uses_the_UserProfile_special_folder()
    {
        var paths = RuntimePaths.ForCurrentUser();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(profile, "PodcastGenerator"), paths.DefaultOutputDirectory);
        Assert.Equal(Path.Combine(profile, ".config", "PodcastGenerator"), paths.RuntimeFolder);
        Assert.DoesNotContain("%USERPROFILE%", paths.RuntimeFolder);
    }
}

public class RuntimeInitializerTests
{
    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly RuntimePaths _paths = new(ServiceFixture.UserProfile);

    // AC-22, D-13
    [Fact]
    public async Task The_folder_and_the_default_style_are_created_when_missing()
    {
        var initializer = new RuntimeInitializer(_fileSystem, _paths, new FakeDefaultResources("DEFAULT {transcript}"));

        await initializer.EnsureAsync(CancellationToken.None);

        Assert.Contains(_paths.RuntimeFolder, _fileSystem.Directories);
        Assert.Equal("DEFAULT {transcript}", _fileSystem.Files[_paths.StyleFilePath]);
    }

    [Fact]
    public async Task An_existing_style_file_is_never_overwritten()
    {
        _fileSystem.Files[_paths.StyleFilePath] = "MY EDIT {transcript}";
        var initializer = new RuntimeInitializer(_fileSystem, _paths, new FakeDefaultResources("DEFAULT {transcript}"));

        await initializer.EnsureAsync(CancellationToken.None);

        Assert.Equal("MY EDIT {transcript}", _fileSystem.Files[_paths.StyleFilePath]);
    }

    [Fact]
    public async Task The_key_file_is_not_created_or_touched()
    {
        var initializer = new RuntimeInitializer(_fileSystem, _paths, new FakeDefaultResources("D {transcript}"));

        await initializer.EnsureAsync(CancellationToken.None);

        Assert.DoesNotContain(_paths.KeyFilePath, _fileSystem.Files.Keys);
    }

    // AC-59: the embedded default is section 9.1 of the style guide, and has no music, Lyria or mixing content.
    [Fact]
    public void The_embedded_default_style_has_the_section_9_1_structure_and_no_music_content()
    {
        var style = new EmbeddedDefaultResources().DefaultStyle;

        Assert.Contains("# AUDIO PROFILE", style);
        Assert.Contains("## THE SCENE", style);
        Assert.Contains("### DIRECTOR'S NOTES", style);
        Assert.Contains("#### TRANSCRIPT", style);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(style, System.Text.RegularExpressions.Regex.Escape(NarrationStyle.TranscriptPlaceholder)));
        Assert.DoesNotContain("music", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lyria", style, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mix", style, StringComparison.OrdinalIgnoreCase);
    }

    // AC-59: the embedded default matches the style guide's section 9.1 text.
    [Fact]
    public void The_embedded_default_matches_section_9_1_of_the_style_guide_except_for_the_placeholder()
    {
        var guide = File.ReadAllText(RepositoryFiles.Combine("docs", "style-guide.md"));
        var start = guide.IndexOf("### 9.1 Narration prompt template", StringComparison.Ordinal);
        var open = guide.IndexOf("```", start, StringComparison.Ordinal) + 3;
        var close = guide.IndexOf("```", open, StringComparison.Ordinal);
        var block = guide[open..close].Replace("\r\n", "\n").Trim('\n');

        var expected = block.Replace("{script text, with only the tags allowed by section 4}", NarrationStyle.TranscriptPlaceholder);
        var actual = new EmbeddedDefaultResources().DefaultStyle.Replace("\r\n", "\n").Trim('\n');

        Assert.Equal(expected, actual);
    }
}

public class StyleProviderTests
{
    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly RuntimePaths _paths = new(ServiceFixture.UserProfile);

    private StyleProvider Create() => new(_fileSystem, _paths);

    // AC-43: the style file is read on every run, so an edit changes the next request.
    [Fact]
    public async Task The_style_is_read_from_the_file_on_every_call()
    {
        _fileSystem.Files[_paths.StyleFilePath] = "FIRST {transcript}";
        var provider = Create();

        var first = await provider.LoadAsync(CancellationToken.None);
        _fileSystem.Files[_paths.StyleFilePath] = "SECOND {transcript}";
        var second = await provider.LoadAsync(CancellationToken.None);

        Assert.Equal("FIRST hello", first.BuildInput("hello"));
        Assert.Equal("SECOND hello", second.BuildInput("hello"));
    }

    // AC-45: the transcript goes where the placeholder is, after the direction.
    [Fact]
    public async Task The_transcript_replaces_the_placeholder_and_is_not_scanned_again()
    {
        _fileSystem.Files[_paths.StyleFilePath] = "# PROFILE\n#### TRANSCRIPT\n{transcript}";

        var style = await Create().LoadAsync(CancellationToken.None);

        Assert.Equal("# PROFILE\n#### TRANSCRIPT\nsay {transcript} literally", style.BuildInput("say {transcript} literally"));
    }

    // AC-44
    [Fact]
    public async Task A_missing_placeholder_is_an_error_that_names_the_file_and_says_deleting_it_restores_the_default()
    {
        _fileSystem.Files[_paths.StyleFilePath] = "no placeholder here";

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => Create().LoadAsync(CancellationToken.None));

        Assert.Contains(_paths.StyleFilePath, exception.Message);
        Assert.Contains("{transcript}", exception.Message);
        Assert.Contains("delete it", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_placeholder_that_appears_twice_is_an_error()
    {
        _fileSystem.Files[_paths.StyleFilePath] = "{transcript} and {transcript}";

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => Create().LoadAsync(CancellationToken.None));

        Assert.Contains("exactly once", exception.Message);
    }

    [Fact]
    public async Task A_missing_style_file_is_an_error_that_names_the_file()
    {
        var exception = await Assert.ThrowsAsync<UserFacingException>(() => Create().LoadAsync(CancellationToken.None));

        Assert.Contains(_paths.StyleFilePath, exception.Message);
    }
}
