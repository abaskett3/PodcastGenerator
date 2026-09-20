namespace PodcastGenerator.Infrastructure.Speech;

/// <summary>Settings of <see cref="OpenRouterSpeechClient"/>.</summary>
/// <param name="RequestTimeout">The longest one attempt may take, from sending the request to the last byte of the audio.
/// It is applied by the client itself because <see cref="HttpClient.Timeout"/> stops applying once the response headers
/// have arrived, and the audio arrives after the headers (the real response is chunked, and a long chunk takes 30 to
/// 80 seconds; see the design doc).</param>
public sealed record SpeechClientOptions(TimeSpan RequestTimeout);
