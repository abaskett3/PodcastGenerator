using System.Globalization;

namespace PodcastGenerator.Application.Narration;

/// <summary>The inline tag strings placed in the transcript. Each mapping is recorded, with the evidence for it, in
/// <c>docs/designs/initial-development.md</c>, "Tags".</summary>
public sealed class TagVocabulary
{
    // Google's Gemini TTS guide documents present-tense tags such as [whispers] (https://ai.google.dev/gemini-api/docs/speech-generation).
    // In the capped verification calls [whispers] produced a whisper in 4 of 5 runs, [whispered] in 1 of 5, so a delivery
    // direction is mapped through this table first. Only directions that were verified are listed.
    private static readonly Dictionary<string, string> VerifiedDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WHISPERED"] = "whispers",
    };

    /// <summary>The tag for a delivery direction such as <c>WHISPERED</c>. A verified direction uses its verified tag;
    /// any other direction becomes its own words, lower-cased, in one tag, so a comma list such as
    /// <c>WHISPERED, SLOW</c> stays in one tag (D-6).</summary>
    public string ForDirection(string direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(direction);
        var trimmed = direction.Trim();
        return VerifiedDirections.TryGetValue(trimmed, out var verified) ? verified : trimmed.ToLowerInvariant();
    }

    /// <summary>The tag for a pause. <paramref name="duration"/> is <see langword="null"/> for <c>(BEAT)</c>.</summary>
    public string ForPause(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "short pause";
        }

        var seconds = duration.Value.TotalSeconds;
        var unit = seconds == 1 ? "second" : "seconds";
        return string.Create(CultureInfo.InvariantCulture, $"pause for {seconds:0.##} {unit}");
    }

    /// <summary>The tag placed before emphasized words.</summary>
    public string Emphasis => "emphasis";
}
