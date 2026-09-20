using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Domain.Security;
using PodcastGenerator.Infrastructure;
using PodcastGenerator.Infrastructure.Speech;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Infrastructure;

/// <summary>These tests replace the HTTP layer with a fake handler. Nothing here reaches the network (AC-5). The canned
/// responses follow the documented behavior: raw audio bytes with an <c>audio/pcm</c> content type on success, and a JSON
/// body <c>{"error": {"code": ..., "message": ...}}</c> on failure (https://openrouter.ai/docs/guides/overview/multimodal/tts and
/// https://openrouter.ai/docs/api-reference/errors). The <c>rate</c> and <c>channels</c> content-type parameters were seen in
/// the capped verification calls recorded in docs/designs/initial-development.md.</summary>
public class OpenRouterSpeechClientTests
{
    private static readonly ApiKey Key = new(TestKeys.Sentinel);

    private static readonly SpeechClientOptions DefaultOptions = new(TimeSpan.FromMinutes(5));

    private static SpeechRequest Request(string input = "hello") =>
        new("google/gemini-3.1-flash-tts-preview", "Umbriel", input, SpeechAudioFormat.Pcm);

    private static (OpenRouterSpeechClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = OpenRouterSpeechClient.BaseAddress };
        return (new OpenRouterSpeechClient(http, DefaultOptions), handler);
    }

    private static HttpResponseMessage Audio(byte[] bytes, string contentType = "audio/pcm; rate=24000; channels=1")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string message, TimeSpan? retryAfter = null)
    {
        var body = JsonSerializer.Serialize(new { error = new { code = (int)status, message } });
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        }

        return response;
    }

    // AC-40
    [Fact]
    public async Task The_request_is_a_POST_to_the_speech_endpoint_with_the_documented_body_fields()
    {
        var (client, handler) = Create(_ => Audio(new byte[8]));

        await client.SynthesizeAsync(Request("Good evening."), Key, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://openrouter.ai/api/v1/audio/speech", sent.Uri.ToString());
        using var json = JsonDocument.Parse(sent.Body);
        var fields = json.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToList();
        Assert.Equal(["input", "model", "response_format", "voice"], fields);
        Assert.Equal("google/gemini-3.1-flash-tts-preview", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("Umbriel", json.RootElement.GetProperty("voice").GetString());
        Assert.Equal("Good evening.", json.RootElement.GetProperty("input").GetString());
        Assert.Equal("pcm", json.RootElement.GetProperty("response_format").GetString());
        Assert.DoesNotContain("input_references", sent.Body);
    }

    [Fact]
    public async Task The_key_is_sent_only_as_a_bearer_authorization_header_never_in_the_url_or_body()
    {
        var (client, handler) = Create(_ => Audio(new byte[8]));

        await client.SynthesizeAsync(Request(), Key, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", sent.AuthorizationScheme);
        Assert.Equal(TestKeys.Sentinel, sent.AuthorizationParameter);
        Assert.DoesNotContain(TestKeys.Sentinel, sent.Uri.ToString());
        Assert.DoesNotContain(TestKeys.Sentinel, sent.Body);
    }

    // AC-41
    [Fact]
    public async Task A_success_is_read_as_raw_audio_bytes_with_the_format_from_the_content_type()
    {
        var bytes = Enumerable.Range(0, 100).Select(value => (byte)value).ToArray();
        var (client, _) = Create(_ => Audio(bytes));

        var audio = await client.SynthesizeAsync(Request(), Key, CancellationToken.None);

        Assert.Equal(bytes, audio.Data.ToArray());
        Assert.Equal(24000, audio.SampleRate);
        Assert.Equal(1, audio.Channels);
    }

    [Fact]
    public async Task Rate_and_channels_come_from_the_content_type_when_it_names_them()
    {
        var (client, _) = Create(_ => Audio(new byte[8], "audio/pcm; rate=16000; channels=1"));

        var audio = await client.SynthesizeAsync(Request(), Key, CancellationToken.None);

        Assert.Equal(16000, audio.SampleRate);
    }

    [Fact]
    public async Task Without_content_type_parameters_the_documented_gemini_layout_is_used()
    {
        // Google documents 24 kHz, 16-bit, mono for Gemini TTS (https://ai.google.dev/gemini-api/docs/speech-generation).
        var (client, _) = Create(_ => Audio(new byte[8], "audio/pcm"));

        var audio = await client.SynthesizeAsync(Request(), Key, CancellationToken.None);

        Assert.Equal(24000, audio.SampleRate);
        Assert.Equal(1, audio.Channels);
    }

    // AC-41, AC-46: a non-success status fails the request, with the API's message.
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.PaymentRequired, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData((HttpStatusCode)429, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task An_error_status_becomes_a_SpeechException_with_the_api_message_and_the_right_retry_class(HttpStatusCode status, bool transient)
    {
        var (client, _) = Create(_ => Error(status, "The API said no"));

        var exception = await Assert.ThrowsAsync<SpeechException>(() => client.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.Equal((int)status, exception.StatusCode);
        Assert.Equal("The API said no", exception.ApiMessage);
        Assert.Equal(transient, exception.IsTransient);
    }

    [Fact]
    public async Task Retry_After_is_read_from_a_429()
    {
        var (client, _) = Create(_ => Error((HttpStatusCode)429, "Slow down", TimeSpan.FromSeconds(60)));

        var exception = await Assert.ThrowsAsync<SpeechException>(() => client.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(60), exception.RetryAfter);
        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task A_402_is_a_wait_and_retry_case_only_when_it_carries_Retry_After()
    {
        // https://openrouter.ai/docs/api-reference/errors: "A 402 without the header is not a wait-and-retry case".
        var (withHeader, _) = Create(_ => Error(HttpStatusCode.PaymentRequired, "In-flight budget", TimeSpan.FromSeconds(5)));
        var (withoutHeader, _) = Create(_ => Error(HttpStatusCode.PaymentRequired, "Insufficient credits"));

        var retryable = await Assert.ThrowsAsync<SpeechException>(() => withHeader.SynthesizeAsync(Request(), Key, CancellationToken.None));
        var plain = await Assert.ThrowsAsync<SpeechException>(() => withoutHeader.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.True(retryable.IsTransient);
        Assert.False(plain.IsTransient);
    }

    [Fact]
    public async Task An_error_body_that_is_not_json_still_fails_with_the_status()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>Bad gateway</html>") });

        var exception = await Assert.ThrowsAsync<SpeechException>(() => client.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.Equal(502, exception.StatusCode);
        Assert.Null(exception.ApiMessage);
        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task Json_on_a_200_is_a_transient_failure_with_the_api_message()
    {
        var (client, _) = Create(_ =>
        {
            var response = Error(HttpStatusCode.OK, "The model returned text");
            return response;
        });

        var exception = await Assert.ThrowsAsync<SpeechException>(() => client.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.Equal("The model returned text", exception.ApiMessage);
        Assert.True(exception.IsTransient);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task An_empty_or_odd_length_audio_body_is_a_transient_failure(int length)
    {
        var (client, _) = Create(_ => Audio(new byte[length]));

        var exception = await Assert.ThrowsAsync<SpeechException>(() => client.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task A_network_error_is_a_transient_failure()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection reset"));

        var exception = await Assert.ThrowsAsync<SpeechException>(() => client.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.True(exception.IsTransient);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task A_timeout_is_a_transient_failure()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("timeout", new TimeoutException()));
        var client = new OpenRouterSpeechClient(new HttpClient(handler) { BaseAddress = OpenRouterSpeechClient.BaseAddress }, DefaultOptions);

        var exception = await Assert.ThrowsAsync<SpeechException>(() => client.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.True(exception.IsTransient);
    }

    // D-12 "network errors, timeouts": the headers arrive (200) but the audio body never finishes. HttpClient.Timeout does not
    // cover this read (the response is read with ResponseHeadersRead), so the client's own time limit must end the attempt as a
    // retryable timeout. WaitAsync makes the test fail rather than hang if the limit is missing.
    [Fact]
    public async Task A_body_that_stalls_after_the_headers_times_out_as_a_transient_failure()
    {
        var handler = new FakeHandler(_ => StalledBody(HttpStatusCode.OK));
        var client = new OpenRouterSpeechClient(
            new HttpClient(handler) { BaseAddress = OpenRouterSpeechClient.BaseAddress },
            new SpeechClientOptions(TimeSpan.FromMilliseconds(200)));

        var exception = await Assert.ThrowsAsync<SpeechException>(
            () => client.SynthesizeAsync(Request(), Key, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.True(exception.IsTransient);
        Assert.Null(exception.StatusCode);
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_error_response_whose_body_stalls_also_times_out_as_a_transient_failure()
    {
        var handler = new FakeHandler(_ => StalledBody(HttpStatusCode.InternalServerError));
        var client = new OpenRouterSpeechClient(
            new HttpClient(handler) { BaseAddress = OpenRouterSpeechClient.BaseAddress },
            new SpeechClientOptions(TimeSpan.FromMilliseconds(200)));

        var exception = await Assert.ThrowsAsync<SpeechException>(
            () => client.SynthesizeAsync(Request(), Key, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.True(exception.IsTransient);
    }

    [Fact]
    public async Task Cancelling_while_the_body_is_being_read_is_a_cancellation_not_a_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new FakeHandler(_ => StalledBody(HttpStatusCode.OK));
        var client = new OpenRouterSpeechClient(
            new HttpClient(handler) { BaseAddress = OpenRouterSpeechClient.BaseAddress },
            new SpeechClientOptions(TimeSpan.FromMinutes(5)));

        var call = client.SynthesizeAsync(Request(), Key, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task A_body_that_arrives_in_time_is_not_cut_short_by_the_limit()
    {
        var handler = new FakeHandler(_ => Audio(new byte[8]));
        var client = new OpenRouterSpeechClient(
            new HttpClient(handler) { BaseAddress = OpenRouterSpeechClient.BaseAddress },
            new SpeechClientOptions(TimeSpan.FromSeconds(30)));

        var audio = await client.SynthesizeAsync(Request(), Key, CancellationToken.None);

        Assert.Equal(8, audio.Data.Length);
    }

    // The registered limit is the documented 5 minutes, and HttpClient.Timeout is off so that there is one clock, not two.
    [Fact]
    public void The_registered_speech_timeout_is_five_minutes_and_the_client_owns_it()
    {
        var services = new ServiceCollection();
        services.AddPodcastGeneratorInfrastructure();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<SpeechClientOptions>();
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ISpeechClient));

        Assert.Equal(TimeSpan.FromMinutes(5), options.RequestTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
        Assert.IsType<OpenRouterSpeechClient>(provider.GetRequiredService<ISpeechClient>());
    }

    // AC-42
    [Fact]
    public async Task An_already_cancelled_token_sends_nothing_and_is_not_turned_into_a_retryable_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var (client, handler) = Create(_ => Audio(new byte[8]));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SynthesizeAsync(Request(), Key, cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Cancelling_while_the_request_is_in_flight_stops_it_with_a_cancellation_not_a_SpeechException()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new OpenRouterSpeechClient(new HttpClient(new HangingHandler(cancellation)) { BaseAddress = OpenRouterSpeechClient.BaseAddress }, DefaultOptions);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SynthesizeAsync(Request(), Key, cancellation.Token));
    }

    // AC-26
    [Fact]
    public async Task No_exception_message_contains_the_key()
    {
        var (client, _) = Create(_ => Error(HttpStatusCode.Unauthorized, "Invalid credentials"));

        var exception = await Assert.ThrowsAsync<SpeechException>(() => client.SynthesizeAsync(Request(), Key, CancellationToken.None));

        Assert.DoesNotContain(TestKeys.Sentinel, exception.ToString());
    }

    /// <summary>A response whose headers arrive at once and whose body never finishes (the connection stalls after the headers).</summary>
    private static HttpResponseMessage StalledBody(HttpStatusCode status)
    {
        var response = new HttpResponseMessage(status) { Content = new StreamContent(new StalledStream()) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/pcm; rate=24000; channels=1");
        return response;
    }

    /// <summary>A read-only stream that never produces a byte and never ends. Only cancellation stops a read.</summary>
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Only ReadAsync is expected.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A server that never answers: Ctrl+C arrives (the token is cancelled) while the request is in flight.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        private readonly CancellationTokenSource _callerCancellation;

        public HangingHandler(CancellationTokenSource callerCancellation)
        {
            _callerCancellation = callerCancellation;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _callerCancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string Body, string? AuthorizationScheme, string? AuthorizationParameter);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!,
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return _respond(request);
        }
    }
}
