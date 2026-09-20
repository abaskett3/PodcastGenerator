using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Narration;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Generation;

public class PodcastGenerationServiceTests
{
    private const string TwoSegmentScript = "### SEGMENT 1\n\nCECIL: First segment.\n\n### SEGMENT 2\n\nCECIL: Second segment.\n";

    private static string SegmentOfLength(int number, int length) => $"CECIL: {new string((char)('a' + number), length)}.\n\n";

    /// <summary>A script with several segments, each nearly a chunk long, so it needs several requests.</summary>
    private static string ManyChunkScript(int chunks)
    {
        var text = string.Empty;
        for (var index = 0; index < chunks; index++)
        {
            text += $"### SEGMENT {index + 1}\n\n{SegmentOfLength(index % 20, NarrationSettings.MaxChunkCharacters - 100)}";
        }

        return text;
    }

    // AC-7, AC-9
    [Fact]
    public async Task A_script_is_narrated_into_one_mp3_in_the_default_directory_and_the_path_is_returned()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.RunAsync();

        Assert.Equal(fixture.DefaultOutputPath, result.OutputPath);
        Assert.Contains(fixture.DefaultOutputPath, fixture.FileSystem.Files.Keys);
        Assert.Contains(fixture.DefaultOutputDirectory, fixture.FileSystem.Directories);
        Assert.Equal(1, result.ChunkCount);
        Assert.Single(fixture.Speech.Requests);
    }

    // AC-40
    [Fact]
    public async Task Each_request_uses_the_fixed_model_voice_and_format_and_never_input_references()
    {
        var fixture = new ServiceFixture();

        await fixture.RunAsync();

        var request = Assert.Single(fixture.Speech.Requests);
        Assert.Equal("google/gemini-3.1-flash-tts-preview", request.Model);
        Assert.Equal("Umbriel", request.Voice);
        Assert.Equal(SpeechAudioFormat.Pcm, request.ResponseFormat);
        Assert.Equal(TestKeys.Sentinel, Assert.Single(fixture.Keys()));
    }

    // AC-45, AC-50: profile, scene, director's notes, then the transcript, identical on every request.
    [Fact]
    public async Task Every_chunk_is_sent_with_the_same_direction_before_its_own_transcript()
    {
        var fixture = new ServiceFixture();
        fixture.SetScript(ManyChunkScript(3));

        var result = await fixture.RunAsync();

        Assert.Equal(3, result.ChunkCount);
        var direction = fixture.Speech.Requests.Select(request => request.Input.Split("#### TRANSCRIPT")[0]).Distinct().ToList();
        Assert.Single(direction);
        Assert.Contains("# AUDIO PROFILE", direction[0]);
        Assert.Contains("### DIRECTOR'S NOTES", direction[0]);
        Assert.All(fixture.Speech.Requests, request => Assert.Matches(@"#### TRANSCRIPT\r?\n", request.Input));
        Assert.Single(fixture.Speech.Requests.Select(request => (request.Model, request.Voice, request.ResponseFormat)).Distinct());
        Assert.Equal(3, fixture.Speech.Requests.Select(request => request.Input).Distinct().Count());
    }

    // AC-50: one after another, in script order. AC-51: n is known before the first request.
    [Fact]
    public async Task Chunks_are_requested_one_at_a_time_in_order_and_progress_shows_k_of_n_before_each_request()
    {
        var fixture = new ServiceFixture();
        fixture.SetScript(ManyChunkScript(3));
        var order = new List<string>();
        fixture.Speech.Handler = (index, request) =>
        {
            order.Add($"request {index + 1}");
            Assert.Equal(index + 1, fixture.Reporter.Chunks.Count); // progress for this chunk was already reported
            return FakeSpeechClient.OneSecondOfSilence();
        };

        await fixture.RunAsync();

        Assert.Equal([(1, 3), (2, 3), (3, 3)], fixture.Reporter.Chunks);
        Assert.Equal(["request 1", "request 2", "request 3"], order);
        Assert.Equal(3, fixture.Speech.Requests.Count);
        Assert.Contains("aaaa", fixture.Speech.Requests[0].Input);
        Assert.Contains("bbbb", fixture.Speech.Requests[1].Input);
        Assert.Contains("cccc", fixture.Speech.Requests[2].Input);
        Assert.Equal(3, fixture.Workspaces.Last!.Chunks.Count);
    }

    // AC-43: editing the style file changes the request text of the next run.
    [Fact]
    public async Task An_edited_style_file_changes_the_next_request_without_a_rebuild()
    {
        var fixture = new ServiceFixture();
        await fixture.RunAsync();
        fixture.FileSystem.Files[fixture.Paths.StyleFilePath] = "MY OWN STYLE\n{transcript}";

        await fixture.RunAsync();

        Assert.Contains("# AUDIO PROFILE", fixture.Speech.Requests[0].Input);
        Assert.StartsWith("MY OWN STYLE\n", fixture.Speech.Requests[1].Input);
    }

    // AC-22
    [Fact]
    public async Task The_runtime_folder_and_default_style_are_created_on_the_first_run()
    {
        var fixture = new ServiceFixture();
        Assert.DoesNotContain(fixture.Paths.StyleFilePath, fixture.FileSystem.Files.Keys);

        await fixture.RunAsync();

        Assert.Contains(fixture.Paths.RuntimeFolder, fixture.FileSystem.Directories);
        Assert.Contains(fixture.Paths.StyleFilePath, fixture.FileSystem.Files.Keys);
    }

    // AC-44
    [Fact]
    public async Task An_unusable_style_file_fails_before_any_request()
    {
        var fixture = new ServiceFixture();
        fixture.FileSystem.Files[fixture.Paths.StyleFilePath] = "no placeholder";

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.RunAsync());

        Assert.Contains(fixture.Paths.StyleFilePath, exception.Message);
        Assert.Empty(fixture.Speech.Requests);
    }

    // AC-10
    [Fact]
    public async Task An_existing_file_is_never_overwritten_and_the_next_free_suffix_is_used()
    {
        var fixture = new ServiceFixture();
        fixture.FileSystem.Files[fixture.DefaultOutputPath] = "PRECIOUS";
        fixture.FileSystem.Files[Path.Combine(fixture.DefaultOutputDirectory, "Podcast-09-19-2026-2.mp3")] = "ALSO PRECIOUS";

        var result = await fixture.RunAsync();

        Assert.Equal(Path.Combine(fixture.DefaultOutputDirectory, "Podcast-09-19-2026-3.mp3"), result.OutputPath);
        Assert.Equal("PRECIOUS", fixture.FileSystem.Files[fixture.DefaultOutputPath]);
        Assert.Equal("ALSO PRECIOUS", fixture.FileSystem.Files[Path.Combine(fixture.DefaultOutputDirectory, "Podcast-09-19-2026-2.mp3")]);
    }

    [Fact]
    public async Task An_explicit_output_file_that_exists_gets_a_numbered_sibling()
    {
        var fixture = new ServiceFixture();
        var requested = Path.Combine(ServiceFixture.UserProfile, "out", "show.mp3");
        fixture.FileSystem.Files[requested] = "PRECIOUS";

        var result = await fixture.RunAsync(requested);

        Assert.Equal(Path.Combine(ServiceFixture.UserProfile, "out", "show-2.mp3"), result.OutputPath);
        Assert.Equal("PRECIOUS", fixture.FileSystem.Files[requested]);
    }

    // AC-11
    [Fact]
    public async Task An_explicit_file_path_is_written_and_its_missing_parent_directories_are_created()
    {
        var fixture = new ServiceFixture();
        var requested = Path.Combine(ServiceFixture.UserProfile, "a", "b", "c", "show.mp3");

        var result = await fixture.RunAsync(requested);

        Assert.Equal(requested, result.OutputPath);
        Assert.Contains(Path.Combine(ServiceFixture.UserProfile, "a", "b", "c"), fixture.FileSystem.Directories);
    }

    // AC-12
    [Fact]
    public async Task An_existing_directory_receives_the_default_file_name()
    {
        var fixture = new ServiceFixture();
        var directory = Path.Combine(ServiceFixture.UserProfile, "existing");
        fixture.FileSystem.Directories.Add(directory);

        var result = await fixture.RunAsync(directory);

        Assert.Equal(Path.Combine(directory, "Podcast-09-19-2026.mp3"), result.OutputPath);
    }

    // AC-13
    [Fact]
    public async Task A_non_mp3_output_path_is_rejected_before_any_request()
    {
        var fixture = new ServiceFixture();

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.RunAsync(Path.Combine(ServiceFixture.UserProfile, "show.wav")));

        Assert.Contains(".mp3", exception.Message);
        Assert.Empty(fixture.Speech.Requests);
    }

    // AC-18
    [Fact]
    public async Task A_missing_script_names_the_path_and_makes_no_request()
    {
        var fixture = new ServiceFixture();
        var missing = Path.Combine(ServiceFixture.UserProfile, "nope.txt");

        var exception = await Assert.ThrowsAsync<UserFacingException>(() =>
            fixture.CreateService().GenerateAsync(new(missing, null), CancellationToken.None));

        Assert.Contains(missing, exception.Message);
        Assert.Empty(fixture.Speech.Requests);
    }

    // AC-19
    [Theory]
    [InlineData("script.md")]
    [InlineData("script.docx")]
    [InlineData("script")]
    [InlineData("script.txt.bak")]
    public async Task Only_txt_scripts_are_accepted(string name)
    {
        var fixture = new ServiceFixture();
        var path = Path.Combine(ServiceFixture.UserProfile, "scripts", name);
        fixture.FileSystem.Files[path] = "CECIL: Hello.";

        var exception = await Assert.ThrowsAsync<UserFacingException>(() =>
            fixture.CreateService().GenerateAsync(new(path, null), CancellationToken.None));

        Assert.Contains(".txt", exception.Message);
        Assert.Empty(fixture.Speech.Requests);
    }

    [Fact]
    public async Task The_txt_extension_is_matched_case_insensitively()
    {
        var fixture = new ServiceFixture();
        var path = Path.Combine(ServiceFixture.UserProfile, "scripts", "SHOUT.TXT");
        fixture.FileSystem.Files[path] = "CECIL: Hello.";

        var result = await fixture.CreateService().GenerateAsync(new(path, null), CancellationToken.None);

        Assert.Equal(1, result.ChunkCount);
    }

    // AC-20
    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("# Title\n\n[SFX: NOISE]\n\n(BEAT)\n\nEND\n")]
    public async Task A_script_with_nothing_to_narrate_is_rejected_before_any_request(string script)
    {
        var fixture = new ServiceFixture();
        fixture.SetScript(script);

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.RunAsync());

        Assert.Contains("nothing to narrate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Speech.Requests);
        Assert.Null(fixture.Workspaces.Last);
    }

    // AC-24
    [Fact]
    public async Task No_key_gives_the_key_file_path_and_the_line_format_and_makes_no_request()
    {
        var fixture = new ServiceFixture(withKeyFile: false);

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.RunAsync());

        Assert.Contains(fixture.Paths.KeyFilePath, exception.Message);
        Assert.Contains("OPENROUTER_API_KEY=<key>", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Speech.Requests);
    }

    [Fact]
    public async Task The_runtime_folder_exists_when_the_missing_key_error_points_to_it()
    {
        // D-13: the folder is created before the key check.
        var fixture = new ServiceFixture(withKeyFile: false);

        await Assert.ThrowsAsync<UserFacingException>(() => fixture.RunAsync());

        Assert.Contains(fixture.Paths.RuntimeFolder, fixture.FileSystem.Directories);
    }

    // AC-23
    [Fact]
    public async Task The_environment_variable_key_is_used_when_there_is_no_key_file()
    {
        var fixture = new ServiceFixture(withKeyFile: false);
        fixture.Environment.Values["OPENROUTER_API_KEY"] = "env-key-value";

        await fixture.RunAsync();

        Assert.Equal("env-key-value", Assert.Single(fixture.Keys()));
    }

    // AC-46, D-12
    [Fact]
    public async Task A_transient_failure_is_retried_and_the_run_then_succeeds()
    {
        var fixture = new ServiceFixture();
        fixture.Speech.Handler = (index, _) => index < 2
            ? throw new SpeechException("busy", 503, "Service unavailable", isTransient: true)
            : FakeSpeechClient.OneSecondOfSilence();

        var result = await fixture.RunAsync();

        Assert.Equal(1, result.ChunkCount);
        Assert.Equal(3, fixture.Speech.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], fixture.Delayer.Delays);
        Assert.Equal(2, fixture.Reporter.Retries.Count);
    }

    [Fact]
    public async Task A_chunk_is_retried_up_to_five_times_then_the_whole_run_fails_and_partial_output_is_removed()
    {
        var fixture = new ServiceFixture();
        fixture.SetScript(ManyChunkScript(3));
        fixture.Speech.Handler = (index, _) => index == 0
            ? FakeSpeechClient.OneSecondOfSilence()
            : throw new SpeechException("bad gateway", 502, "The model is down", isTransient: true);

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.RunAsync());

        // Chunk 1 succeeded (1 request); chunk 2 was tried once plus 5 retries (6 requests); chunk 3 was never tried.
        Assert.Equal(7, fixture.Speech.Requests.Count);
        Assert.Equal(5, fixture.Delayer.Delays.Count);
        Assert.Equal([2, 4, 8, 16, 30], fixture.Delayer.Delays.Select(delay => (int)delay.TotalSeconds));
        Assert.Contains("chunk 2 of 3", exception.Message);
        Assert.Contains("502", exception.Message);
        Assert.Contains("The model is down", exception.Message);
        Assert.DoesNotContain(fixture.FileSystem.Files.Keys, path => path.EndsWith(".mp3", StringComparison.Ordinal) || path.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.True(fixture.Workspaces.Last!.Disposed);
    }

    [Theory]
    [InlineData(401, "Invalid credentials")]
    [InlineData(402, "Insufficient credits")]
    [InlineData(400, "Bad request")]
    [InlineData(403, "Forbidden")]
    public async Task A_failure_that_cannot_pass_fails_at_once_without_retries(int status, string apiMessage)
    {
        var fixture = new ServiceFixture();
        fixture.Speech.Handler = (_, _) => throw new SpeechException("no", status, apiMessage, isTransient: false);

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.RunAsync());

        Assert.Single(fixture.Speech.Requests);
        Assert.Empty(fixture.Delayer.Delays);
        Assert.Contains("chunk 1 of 1", exception.Message);
        Assert.Contains($"HTTP {status}", exception.Message);
        Assert.Contains(apiMessage, exception.Message);
    }

    [Fact]
    public async Task The_wait_the_server_asks_for_is_honored_up_to_a_cap()
    {
        var fixture = new ServiceFixture();
        fixture.Speech.Handler = (index, _) => index switch
        {
            0 => throw new SpeechException("slow down", 429, "Rate limited", isTransient: true, retryAfter: TimeSpan.FromSeconds(7)),
            1 => throw new SpeechException("slow down", 429, "Rate limited", isTransient: true, retryAfter: TimeSpan.FromHours(3)),
            _ => FakeSpeechClient.OneSecondOfSilence(),
        };

        await fixture.RunAsync();

        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(120)], fixture.Delayer.Delays);
    }

    // AC-26
    [Fact]
    public async Task A_key_echoed_by_the_server_is_redacted_from_the_error_message()
    {
        var fixture = new ServiceFixture();
        fixture.Speech.Handler = (_, _) =>
            throw new SpeechException("no", 401, $"Invalid key {TestKeys.Sentinel} provided", isTransient: false);

        var exception = await Assert.ThrowsAsync<UserFacingException>(() => fixture.RunAsync());

        Assert.DoesNotContain(TestKeys.Sentinel, exception.Message);
        Assert.Contains("[redacted]", exception.Message);
    }

    [Fact]
    public async Task The_key_never_appears_in_any_reported_output_on_success_or_failure()
    {
        var fixture = new ServiceFixture();
        fixture.Speech.Handler = (index, _) => index == 0
            ? throw new SpeechException("busy", 503, $"busy {TestKeys.Sentinel}", isTransient: true)
            : FakeSpeechClient.OneSecondOfSilence();

        var result = await fixture.RunAsync();

        var everything = string.Join('\n', fixture.Reporter.Events.Concat(fixture.Reporter.Retries).Concat(fixture.Reporter.Warnings).Append(result.OutputPath));
        Assert.DoesNotContain(TestKeys.Sentinel, everything);
        Assert.DoesNotContain(fixture.FileSystem.Files.Where(file => file.Key != fixture.Paths.KeyFilePath).Select(file => file.Value), text => text.Contains(TestKeys.Sentinel, StringComparison.Ordinal));
    }

    // AC-14
    [Fact]
    public async Task If_encoding_fails_after_the_file_was_created_it_is_deleted_and_nothing_else_is_touched()
    {
        var fixture = new ServiceFixture();
        fixture.FileSystem.Files[fixture.DefaultOutputPath] = "PRECIOUS";
        fixture.Workspaces.FailWhileWriting = new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => fixture.RunAsync());

        Assert.Equal("PRECIOUS", fixture.FileSystem.Files[fixture.DefaultOutputPath]);
        Assert.Equal(["PRECIOUS"], fixture.FileSystem.Files.Where(file => file.Key.StartsWith(fixture.DefaultOutputDirectory, StringComparison.Ordinal)).Select(file => file.Value));
        Assert.True(fixture.Workspaces.Last!.Disposed);
    }

    [Fact]
    public async Task A_temporary_file_that_cannot_be_removed_does_not_hide_the_result_and_is_reported()
    {
        var fixture = new ServiceFixture();
        fixture.FileSystem.DeleteFailure = new IOException("locked");

        var result = await fixture.RunAsync();

        Assert.Equal(fixture.DefaultOutputPath, result.OutputPath);
        Assert.Contains("could not be removed", Assert.Single(fixture.Reporter.Warnings));
    }

    // AC-42
    [Fact]
    public async Task An_already_cancelled_token_sends_no_request_and_leaves_no_file()
    {
        var fixture = new ServiceFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunAsync(cancellationToken: cancellation.Token));

        Assert.Empty(fixture.Speech.Requests);
        Assert.DoesNotContain(fixture.DefaultOutputPath, fixture.FileSystem.Files.Keys);
    }

    [Fact]
    public async Task Cancelling_during_a_run_stops_further_requests_and_removes_partial_output()
    {
        var fixture = new ServiceFixture();
        fixture.SetScript(ManyChunkScript(4));
        using var cancellation = new CancellationTokenSource();
        fixture.Speech.Handler = (index, _) =>
        {
            if (index == 1)
            {
                cancellation.Cancel(); // Ctrl+C while the second request is in flight
            }

            return FakeSpeechClient.OneSecondOfSilence();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.RunAsync(cancellationToken: cancellation.Token));

        Assert.Equal(2, fixture.Speech.Requests.Count);
        Assert.DoesNotContain(fixture.FileSystem.Files.Keys, path => path.EndsWith(".mp3", StringComparison.Ordinal));
        Assert.True(fixture.Workspaces.Last!.Disposed);
    }

    [Fact]
    public async Task The_cancellation_token_reaches_every_request()
    {
        var fixture = new ServiceFixture();
        fixture.SetScript(ManyChunkScript(2));
        using var cancellation = new CancellationTokenSource();

        await fixture.RunAsync(cancellationToken: cancellation.Token);

        Assert.All(fixture.Speech.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    // AC-48: an oversized paragraph is split at a sentence end, with a warning.
    [Fact]
    public async Task A_warning_is_reported_when_a_paragraph_is_split()
    {
        var fixture = new ServiceFixture();
        var sentence = new string('x', 700) + ". ";
        fixture.SetScript($"CECIL: {string.Concat(Enumerable.Repeat(sentence, 5)).Trim()}\n");

        var result = await fixture.RunAsync();

        Assert.True(result.ChunkCount > 1);
        Assert.Contains("split at sentence ends", Assert.Single(fixture.Reporter.Warnings));
    }

    // AC-52: no chunk is missing, duplicated or reordered.
    [Fact]
    public async Task Every_chunks_audio_is_added_once_in_script_order()
    {
        var fixture = new ServiceFixture();
        fixture.SetScript(ManyChunkScript(5));
        fixture.Speech.Handler = (index, _) => new SpeechAudio(new byte[(index + 1) * 2], 24000, 1);

        await fixture.RunAsync();

        Assert.Equal([2, 4, 6, 8, 10], fixture.Workspaces.Last!.Chunks.Select(chunk => chunk.Data.Length));
    }

    // AC-21
    [Fact]
    public async Task A_script_of_three_thousand_lines_is_processed_to_completion()
    {
        var fixture = new ServiceFixture();
        var lines = new List<string>();
        for (var segment = 1; segment <= 100; segment++)
        {
            lines.Add($"### SEGMENT {segment}");
            lines.Add(string.Empty);
            for (var paragraph = 1; paragraph <= 14; paragraph++)
            {
                lines.Add($"CECIL: Paragraph {paragraph} of segment {segment}, with a few more words to make it a little longer.");
                lines.Add(string.Empty);
            }
        }

        Assert.True(lines.Count >= 3000);
        fixture.SetScript(string.Join('\n', lines));

        var result = await fixture.RunAsync();

        Assert.True(result.ChunkCount > 1);
        Assert.Equal(result.ChunkCount, fixture.Speech.Requests.Count);
        Assert.Equal(result.ChunkCount, fixture.Workspaces.Last!.Chunks.Count);
    }

    // AC-36: the same script gives the same request text on every run.
    [Fact]
    public async Task The_same_script_gives_the_same_request_text_every_time()
    {
        var fixture = new ServiceFixture();
        fixture.SetScript(TwoSegmentScript);

        await fixture.RunAsync();
        await fixture.RunAsync();

        Assert.Equal(fixture.Speech.Requests[0].Input, fixture.Speech.Requests[1].Input);
    }

    // AC-47, AC-59: what the model is told contains no real person and no music content.
    [Fact]
    public async Task The_default_direction_names_no_real_person_and_has_no_music_content()
    {
        var fixture = new ServiceFixture();

        await fixture.RunAsync();

        var direction = fixture.Speech.Requests[0].Input.Split("#### TRANSCRIPT")[0];
        Assert.DoesNotContain("Cecil", direction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Night Vale", direction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("music", direction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("American", direction);
    }
}

internal static class ServiceFixtureExtensions
{
    public static IReadOnlyList<string> Keys(this ServiceFixture fixture) => fixture.Speech.Keys.Distinct().ToList();
}
