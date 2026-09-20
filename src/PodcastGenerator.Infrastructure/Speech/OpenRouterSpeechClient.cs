using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Domain.Security;

namespace PodcastGenerator.Infrastructure.Speech;

/// <summary>Calls OpenRouter's <c>POST /api/v1/audio/speech</c>. The documented request body has <c>model</c>, <c>input</c>,
/// <c>voice</c> and <c>response_format</c> (https://openrouter.ai/docs/guides/overview/multimodal/tts); the response is raw
/// audio bytes, and errors are JSON with <c>error.code</c> and <c>error.message</c>
/// (https://openrouter.ai/docs/api-reference/errors). This class never logs and never puts the key in an exception.</summary>
public sealed class OpenRouterSpeechClient : ISpeechClient
{
    /// <summary>The base address of the OpenRouter API. It ends with a slash so relative paths append to it.</summary>
    public static readonly Uri BaseAddress = new("https://openrouter.ai/api/v1/");

    /// <summary>The path of the speech endpoint, relative to <see cref="BaseAddress"/>.</summary>
    public const string SpeechPath = "audio/speech";

    private readonly HttpClient _httpClient;

    public OpenRouterSpeechClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SpeechAudio> SynthesizeAsync(SpeechRequest request, ApiKey apiKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(apiKey);

        using var message = new HttpRequestMessage(HttpMethod.Post, SpeechPath)
        {
            Content = JsonContent.Create(
                new SpeechBody(request.Model, request.Input, request.Voice, ToWireFormat(request.ResponseFormat)),
                options: SerializerOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Reveal());

        try
        {
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw await FailureAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return ToAudio(response, bytes);
        }
        catch (HttpRequestException exception)
        {
            throw new SpeechException($"The request could not be completed ({exception.GetType().Name}).", null, null, isTransient: true, inner: exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the caller's cancellation: the HttpClient timeout elapsed.
            throw new SpeechException("The request timed out.", null, null, isTransient: true, inner: exception);
        }
    }

    private static SpeechAudio ToAudio(HttpResponseMessage response, byte[] bytes)
    {
        var contentType = response.Content.Headers.ContentType;
        if (string.Equals(contentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            // The docs say a success is raw audio; JSON on a 200 is an error the model emitted while generating.
            throw new SpeechException(
                "The speech endpoint answered with JSON instead of audio.",
                (int)response.StatusCode,
                ParseApiMessage(bytes),
                isTransient: true);
        }

        if (bytes.Length == 0 || bytes.Length % SpeechAudioConstants.BytesPerSample != 0)
        {
            throw new SpeechException(
                $"The speech endpoint returned an unusable audio body ({bytes.Length} bytes).",
                (int)response.StatusCode,
                null,
                isTransient: true);
        }

        // Observed in the capped verification calls: "Content-Type: audio/pcm; rate=24000; channels=1". Google documents the
        // same layout for Gemini TTS (24 kHz, 16-bit, mono), which is the fallback when a parameter is absent.
        var sampleRate = ReadIntParameter(contentType, "rate") ?? SpeechAudioConstants.SampleRate;
        var channels = ReadIntParameter(contentType, "channels") ?? SpeechAudioConstants.Channels;
        return new SpeechAudio(bytes, sampleRate, channels);
    }

    private static int? ReadIntParameter(MediaTypeHeaderValue? contentType, string name)
    {
        var parameter = contentType?.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return int.TryParse(parameter?.Value?.Trim('"'), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value > 0
            ? value
            : null;
    }

    private static async Task<SpeechException> FailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        byte[] body;
        try
        {
            body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            body = [];
        }

        var apiMessage = ParseApiMessage(body);
        var retryAfter = ReadRetryAfter(response);

        // Transient: timeouts, rate limits and server-side failures (D-12). OpenRouter documents that a 402 is a
        // wait-and-retry case only when it carries a Retry-After header; without one it is a plain credit failure.
        var isTransient = status is 408 or 429 or >= 500 || (status == 402 && retryAfter is not null);

        return new SpeechException(
            $"The speech endpoint answered {status} {response.ReasonPhrase}.".TrimEnd(),
            status,
            apiMessage,
            isTransient,
            retryAfter);
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    /// <summary>Reads <c>error.message</c> from OpenRouter's JSON error body, or returns <see langword="null"/>.</summary>
    internal static string? ParseApiMessage(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON (for example an HTML error page from a proxy): there is no API message.
        }

        return null;
    }

    private static string ToWireFormat(SpeechAudioFormat format) => format switch
    {
        SpeechAudioFormat.Pcm => "pcm",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported audio format."),
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record SpeechBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("voice")] string Voice,
        [property: JsonPropertyName("response_format")] string ResponseFormat);
}

/// <summary>The PCM layout of the speech endpoint's <c>pcm</c> response. See the design doc, "Verified API facts".</summary>
internal static class SpeechAudioConstants
{
    public const int SampleRate = 24000;
    public const int Channels = 1;
    public const int BytesPerSample = 2;
}
