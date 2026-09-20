using System.Net;
using PodcastGenerator.Application.Narration;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Failures over the (fake) HTTP layer with the real file system and MP3 encoder: retries, the failed run, cleanup
/// and cancellation (AC-14, AC-15, AC-41, AC-42, AC-46). Error bodies follow the documented shape
/// <c>{"error": {"code", "message"}}</c>; the message text is arbitrary test text.</summary>
public sealed class FailureAndRetryTests : IDisposable
{
    private static readonly string SamplePath = RepositoryFiles.Combine("docs", "sample-scripts", "QE-3395-RC-09-17-26-nightvale-podcast-script.txt");

    private readonly Pipeline _pipeline = new();

    public void Dispose() => _pipeline.Dispose();

    private string OneLineScript() => _pipeline.WriteScript("CECIL: Good evening.\n");

    private static HttpResponseMessage Ok() => CannedSpeechHandler.Audio(Pcm.Tone(440, 0.5));

    private void AssertNothingLeftBehind(string outputDirectory)
    {
        Assert.DoesNotContain(Pipeline.Entries(outputDirectory), entry => entry.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Pipeline.Entries(outputDirectory), entry => entry.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        Assert.True(_pipeline.AllWorkspaceFoldersDeleted(), "The temporary audio folder must be deleted.");
    }

    // AC-46, D-12: a transient failure is retried; up to 5 retries.
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_transient_status_is_retried_and_the_run_then_succeeds(HttpStatusCode status)
    {
        _pipeline.Http.Responder = request => request.Number <= 2 ? CannedSpeechHandler.Error(status, "try later (test text)") : Ok();

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(3, _pipeline.Http.Requests.Count);
        Assert.Equal(2, _pipeline.Delayer.Delays.Count);
        Assert.True(File.Exists(_pipeline.DefaultOutputPath));

        // AC-50: a retry sends the very same request.
        Assert.Single(_pipeline.Http.Requests.Select(request => request.Body).Distinct());
    }

    [Fact]
    public async Task Five_failures_are_retried_and_the_sixth_attempt_can_succeed_with_a_growing_wait()
    {
        _pipeline.Http.Responder = request => request.Number <= 5 ? CannedSpeechHandler.Error(HttpStatusCode.ServiceUnavailable, "busy (test text)") : Ok();

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(6, _pipeline.Http.Requests.Count);
        var delays = _pipeline.Delayer.Delays;
        Assert.Equal(5, delays.Count);
        Assert.All(delays, delay => Assert.True(delay > TimeSpan.Zero));
        Assert.Equal(delays.Order().ToList(), delays);
        Assert.True(delays[^1] > delays[0], "The wait must increase.");
    }

    // AC-46: after the retries the whole run fails, naming chunk k of n, the status and the API's message; nothing is kept.
    [Fact]
    public async Task A_chunk_that_never_succeeds_fails_the_run_after_five_retries_and_leaves_nothing_behind()
    {
        _pipeline.Http.Responder = _ => CannedSpeechHandler.Error(HttpStatusCode.InternalServerError, "model unavailable (test text)");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.Equal(6, _pipeline.Http.Requests.Count);
        Assert.Empty(_pipeline.Out.ToString());
        var error = _pipeline.Err.ToString();
        Assert.Contains("chunk 1 of 1", error, StringComparison.Ordinal);
        Assert.Contains("500", error, StringComparison.Ordinal);
        Assert.Contains("model unavailable (test text)", error, StringComparison.Ordinal);
        AssertNothingLeftBehind(_pipeline.DefaultOutputDirectory);
    }

    // AC-14, AC-46: a failure in a later chunk discards the earlier chunks' audio and a pre-existing file is untouched.
    [Fact]
    public async Task A_failure_in_the_third_chunk_names_it_and_keeps_no_output_and_no_existing_file_is_touched()
    {
        var outputDirectory = Path.Combine(_pipeline.Root, "out");
        Directory.CreateDirectory(outputDirectory);
        var existing = Path.Combine(outputDirectory, "show.mp3");
        File.WriteAllText(existing, "previous episode");
        _pipeline.Http.Responder = request => request.Number == 3 ? CannedSpeechHandler.Error(HttpStatusCode.BadRequest, "bad input (test text)") : Ok();

        var exitCode = await _pipeline.RunAsync(SamplePath, existing);

        Assert.Equal(1, exitCode);
        Assert.Equal(3, _pipeline.Http.Requests.Count);
        var error = _pipeline.Err.ToString();
        Assert.Matches(@"chunk 3 of \d+", error);
        Assert.Contains("400", error, StringComparison.Ordinal);
        Assert.Contains("bad input (test text)", error, StringComparison.Ordinal);
        Assert.Equal(["show.mp3"], Pipeline.Entries(outputDirectory));
        Assert.Equal("previous episode", File.ReadAllText(existing));
        Assert.True(_pipeline.AllWorkspaceFoldersDeleted());
    }

    // D-12: other client errors fail at once.
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_client_error_fails_at_once_without_a_retry(HttpStatusCode status)
    {
        _pipeline.Http.Responder = _ => CannedSpeechHandler.Error(status, "rejected (test text)");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.Single(_pipeline.Http.Requests);
        Assert.Empty(_pipeline.Delayer.Delays);
        Assert.Contains(((int)status).ToString(), _pipeline.Err.ToString(), StringComparison.Ordinal);
        AssertNothingLeftBehind(_pipeline.DefaultOutputDirectory);
    }

    // D-12: a Retry-After header is respected.
    [Fact]
    public async Task A_retry_after_header_sets_the_wait()
    {
        _pipeline.Http.Responder = request => request.Number == 1
            ? CannedSpeechHandler.Error(HttpStatusCode.TooManyRequests, "slow down (test text)", TimeSpan.FromSeconds(7))
            : Ok();

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal([TimeSpan.FromSeconds(7)], _pipeline.Delayer.Delays);
    }

    // D-12: network errors and timeouts are retried.
    [Fact]
    public async Task A_network_error_and_a_timeout_are_retried()
    {
        _pipeline.Http.Responder = request => request.Number switch
        {
            1 => throw new HttpRequestException("connection reset (test)"),
            2 => throw new TaskCanceledException("timed out (test)", new TimeoutException()),
            _ => Ok(),
        };

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(3, _pipeline.Http.Requests.Count);
    }

    [Fact]
    public async Task A_network_that_never_answers_fails_the_run_with_a_message()
    {
        _pipeline.Http.Responder = _ => throw new HttpRequestException("no route (test)");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        Assert.Equal(6, _pipeline.Http.Requests.Count);
        Assert.Contains("chunk 1 of 1", _pipeline.Err.ToString(), StringComparison.Ordinal);
        AssertNothingLeftBehind(_pipeline.DefaultOutputDirectory);
    }

    // AC-41: a success is raw audio. A JSON body, or a body that is not 16-bit audio, is never written out as an MP3.
    [Fact]
    public async Task A_json_body_on_a_200_is_never_turned_into_an_mp3()
    {
        _pipeline.Http.Responder = _ => CannedSpeechHandler.Error(HttpStatusCode.OK, "generation failed (test text)");

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        AssertNothingLeftBehind(_pipeline.DefaultOutputDirectory);
    }

    [Fact]
    public async Task An_empty_audio_body_is_never_turned_into_an_mp3()
    {
        _pipeline.Http.Responder = _ => CannedSpeechHandler.Audio([]);

        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(1, exitCode);
        AssertNothingLeftBehind(_pipeline.DefaultOutputDirectory);
    }

    // AC-42, AC-14: Ctrl+C reaches the HTTP call, no further request is sent, and nothing is kept.
    [Fact]
    public async Task Cancelling_during_the_second_request_sends_no_third_request_and_leaves_nothing_behind()
    {
        using var cancellation = new CancellationTokenSource();
        _pipeline.Http.Responder = request =>
        {
            if (request.Number == 2)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            return Ok();
        };

        var exitCode = await _pipeline.RunAsync(SamplePath, cancellationToken: cancellation.Token);

        Assert.Equal(1, exitCode);
        Assert.Equal(2, _pipeline.Http.Requests.Count);
        Assert.Contains("cancel", _pipeline.Err.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_pipeline.Out.ToString());
        AssertNothingLeftBehind(_pipeline.DefaultOutputDirectory);
    }

    [Fact]
    public async Task An_already_cancelled_token_sends_no_request()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exitCode = await _pipeline.RunAsync(OneLineScript(), cancellationToken: cancellation.Token);

        Assert.Equal(1, exitCode);
        Assert.Empty(_pipeline.Http.Requests);
        AssertNothingLeftBehind(_pipeline.DefaultOutputDirectory);
    }

    [Fact]
    public async Task Cancelling_while_waiting_to_retry_sends_no_further_request()
    {
        using var cancellation = new CancellationTokenSource();
        _pipeline.Http.Responder = _ =>
        {
            cancellation.Cancel();
            return CannedSpeechHandler.Error(HttpStatusCode.ServiceUnavailable, "busy (test text)");
        };

        var exitCode = await _pipeline.RunAsync(OneLineScript(), cancellationToken: cancellation.Token);

        Assert.Equal(1, exitCode);
        Assert.Single(_pipeline.Http.Requests);
        AssertNothingLeftBehind(_pipeline.DefaultOutputDirectory);
    }

    // AC-14: a failure while encoding (a directory where the output must go is not possible; use an unwritable name) is
    // covered by the unit tests; here the real encoder runs and its temporary file is moved, not left.
    [Fact]
    public async Task A_successful_run_leaves_only_the_mp3_and_no_temporary_files()
    {
        var exitCode = await _pipeline.RunAsync(OneLineScript());

        Assert.Equal(0, exitCode);
        Assert.Equal(["Podcast-09-19-2026.mp3"], Pipeline.Entries(_pipeline.DefaultOutputDirectory));
        Assert.True(_pipeline.AllWorkspaceFoldersDeleted());
    }

    // The retry limit is the documented 5 (CLAUDE.md, "Usage").
    [Fact]
    public void The_retry_limit_is_five()
    {
        Assert.Equal(5, NarrationSettings.MaxRetries);
    }
}
