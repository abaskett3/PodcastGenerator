namespace PodcastGenerator.Application.Narration;

/// <summary>Fixed narration settings. The voice is not configurable in 1.0.0 (D-22); the chunk size is set from the
/// measurements recorded in <c>docs/designs/initial-development.md</c>.</summary>
public static class NarrationSettings
{
    /// <summary>The only speech model used (CLAUDE.md, "Gotchas").</summary>
    public const string Model = "google/gemini-3.1-flash-tts-preview";

    /// <summary>The narrator voice (CLAUDE.md, "Usage"). OpenRouter lists it in the model's <c>supported_voices</c>.</summary>
    public const string Voice = "Umbriel";

    /// <summary>A chunk is retried up to this many times (so up to <c>MaxRetries + 1</c> attempts) before the run fails.</summary>
    public const int MaxRetries = 5;

    /// <summary>The largest transcript, in characters, sent in one request. See the design doc, "Chunk size".</summary>
    public const int MaxChunkCharacters = 1500;
}
