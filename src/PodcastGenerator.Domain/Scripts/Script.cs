namespace PodcastGenerator.Domain.Scripts;

/// <summary>A run of spoken text inside a paragraph. <see cref="IsEmphasized"/> marks words the script wrote in
/// <c>*asterisks*</c>; the asterisks themselves are not part of <see cref="Text"/>.</summary>
public sealed record TextSpan(string Text, bool IsEmphasized);

/// <summary>One element of a script that produces narration. Cues, headings, header lines and speaker names never
/// appear here: the parser removes them.</summary>
public abstract record ScriptBlock;

/// <summary>A paragraph to be narrated. <see cref="Direction"/> is the delivery direction that followed the speaker name
/// (for example <c>WHISPERED</c>), or <see langword="null"/>.</summary>
public sealed record SpeechBlock(string? Direction, IReadOnlyList<TextSpan> Spans) : ScriptBlock
{
    /// <summary>The spoken words with emphasis markers removed.</summary>
    public string PlainText => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>A pause written on a line of its own. <see cref="Duration"/> is <see langword="null"/> for <c>(BEAT)</c>.</summary>
public sealed record PauseBlock(TimeSpan? Duration) : ScriptBlock;

/// <summary>The blocks between two segment headings (or the whole script when it has none).</summary>
public sealed record ScriptSegment(IReadOnlyList<ScriptBlock> Blocks);

/// <summary>A parsed script.</summary>
public sealed record Script(IReadOnlyList<ScriptSegment> Segments)
{
    /// <summary>True when at least one block carries spoken text.</summary>
    public bool HasSpeech => Segments.Any(segment => segment.Blocks.OfType<SpeechBlock>().Any());
}
