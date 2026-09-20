namespace PodcastGenerator.Application.Abstractions;

/// <summary>A speech request failed.</summary>
public sealed class SpeechException : Exception
{
    public SpeechException(string message, int? statusCode, string? apiMessage, bool isTransient, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ApiMessage = apiMessage;
        IsTransient = isTransient;
        RetryAfter = retryAfter;
    }

    /// <summary>The HTTP status code, when the server answered.</summary>
    public int? StatusCode { get; }

    /// <summary>The <c>error.message</c> from the API's JSON error body, when there was one.</summary>
    public string? ApiMessage { get; }

    /// <summary>True for failures that can go away on their own (network errors, timeouts, 408, 429, 5xx).</summary>
    public bool IsTransient { get; }

    /// <summary>The wait the server asked for with a <c>Retry-After</c> header, when it sent one.</summary>
    public TimeSpan? RetryAfter { get; }
}
