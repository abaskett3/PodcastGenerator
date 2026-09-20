using PodcastGenerator.Domain.Security;

namespace PodcastGenerator.Application.Abstractions;

/// <summary>The audio format requested from, and returned by, the speech endpoint.</summary>
public enum SpeechAudioFormat
{
    /// <summary>Raw PCM. The sample rate, bit depth and channel count are given by <see cref="SpeechAudio"/>.</summary>
    Pcm,
}

public sealed record SpeechRequest(string Model, string Voice, string Input, SpeechAudioFormat ResponseFormat);

/// <summary>Raw PCM audio returned by the speech endpoint: signed 16-bit little-endian samples.</summary>
public sealed record SpeechAudio(ReadOnlyMemory<byte> Data, int SampleRate, int Channels);

/// <summary>Implemented in Infrastructure: the only place that talks to OpenRouter.</summary>
public interface ISpeechClient
{
    /// <exception cref="SpeechException">The request failed. <see cref="SpeechException.IsTransient"/> says whether
    /// trying again can help.</exception>
    Task<SpeechAudio> SynthesizeAsync(SpeechRequest request, ApiKey apiKey, CancellationToken cancellationToken);
}
