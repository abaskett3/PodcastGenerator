using System.Globalization;
using System.Text.RegularExpressions;
using PodcastGenerator.Domain.Scripts;

namespace PodcastGenerator.Application.Scripts;

/// <summary>Turns script text into <see cref="Script"/> blocks. Rules come from <c>docs/script-writing-guide.md</c> and
/// the acceptance criteria AC-28 to AC-36 of <c>docs/specs/initial-development.md</c>. Every non-blank line is one element
/// (guide layout rule 1), so a line is classified on its own.</summary>
public sealed partial class ScriptParser : IScriptParser
{
    // A heading: "#", "##" or "###" followed by a space (or nothing). "#1 hit" is not a heading.
    [GeneratedRegex(@"^#{1,6}(\s|$)")]
    private static partial Regex HeadingRegex();

    // "###" exactly: a segment heading (guide skeleton). "##" is a scene heading.
    [GeneratedRegex(@"^###(\s|$)")]
    private static partial Regex SegmentHeadingRegex();

    // The first "##" or deeper heading ends the header block (guide conformance item 1).
    [GeneratedRegex(@"^#{2,6}(\s|$)")]
    private static partial Regex SceneOrSegmentHeadingRegex();

    // A whole-line cue: "[SFX: ...]", "[MUSIC: ...]", "[FADE OUT]".
    [GeneratedRegex(@"^\[.*\]$")]
    private static partial Regex CueRegex();

    // "(BEAT)" or "(PAUSE - 3 SECONDS)" on a line of its own (D-8).
    [GeneratedRegex(@"^\(\s*(?:(?<beat>BEAT)|PAUSE\s*[-–—]\s*(?<seconds>\d+(?:\.\d+)?)\s*SECONDS?)\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex PauseRegex();

    // The four header labels named by the user (AC-29). Upper case only, as the guide writes them.
    [GeneratedRegex(@"^(PROGRAMME|EPISODE|STYLE|CAST)\s*:")]
    private static partial Regex NamedHeaderRegex();

    // "NAME: rest", with an all-capitals name made of letters, spaces, apostrophes, hyphens and periods (D-5).
    [GeneratedRegex(@"^(?<name>\p{Lu}[\p{Lu} .'’\-]*?)\s*:\s*(?<rest>.*)$")]
    private static partial Regex SpeakerLineRegex();

    // After the speaker name: an optional "(CONT'D)" (D-3), then an optional "(DIRECTION)" (D-6), then the text.
    [GeneratedRegex(@"^(?:\(\s*CONT['’]?D\s*\)\s*)?(?:\((?<direction>[^()]*)\)\s*)?(?<text>.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AfterSpeakerRegex();

    [GeneratedRegex(@"\*(?<emphasized>[^*]+)\*")]
    private static partial Regex EmphasisRegex();

    [GeneratedRegex(@"\r\n|\n|\r")]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public Script Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = LineBreakRegex().Split(text.TrimStart('﻿')).Select(line => line.Trim()).ToArray();

        // D-4: label lines before the first scene or segment heading are header lines. When a script has no such
        // heading at all there is no header block, so only the four named labels are dropped and the rest is speech.
        var firstHeadingIndex = Array.FindIndex(lines, line => SceneOrSegmentHeadingRegex().IsMatch(line));

        var segments = new List<ScriptSegment>();
        var blocks = new List<ScriptBlock>();

        void CloseSegment()
        {
            if (blocks.Count > 0)
            {
                segments.Add(new ScriptSegment(blocks.ToArray()));
                blocks.Clear();
            }
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            if (HeadingRegex().IsMatch(line))
            {
                if (SegmentHeadingRegex().IsMatch(line))
                {
                    CloseSegment();
                }

                continue;
            }

            if (line == "END" || CueRegex().IsMatch(line) || NamedHeaderRegex().IsMatch(line))
            {
                continue;
            }

            var pause = PauseRegex().Match(line);
            if (pause.Success)
            {
                TimeSpan? duration = pause.Groups["beat"].Success
                    ? null
                    : TimeSpan.FromSeconds(double.Parse(pause.Groups["seconds"].Value, CultureInfo.InvariantCulture));
                blocks.Add(new PauseBlock(duration));
                continue;
            }

            string? direction = null;
            var spoken = line;

            var speaker = SpeakerLineRegex().Match(line);
            if (speaker.Success)
            {
                if (firstHeadingIndex >= 0 && index < firstHeadingIndex)
                {
                    continue; // header line such as "DURATION: 10 minutes"
                }

                var after = AfterSpeakerRegex().Match(speaker.Groups["rest"].Value);
                var rawDirection = after.Groups["direction"].Success
                    ? WhitespaceRegex().Replace(after.Groups["direction"].Value, " ").Trim()
                    : string.Empty;
                direction = rawDirection.Length > 0 ? rawDirection : null;
                spoken = after.Groups["text"].Value.Trim();
            }

            var spans = SplitEmphasis(spoken);
            if (spans.All(span => string.IsNullOrWhiteSpace(span.Text)))
            {
                continue;
            }

            blocks.Add(new SpeechBlock(direction, spans));
        }

        CloseSegment();
        return new Script(segments);
    }

    private static List<TextSpan> SplitEmphasis(string text)
    {
        var spans = new List<TextSpan>();
        var position = 0;
        foreach (Match match in EmphasisRegex().Matches(text))
        {
            if (match.Index > position)
            {
                spans.Add(new TextSpan(text[position..match.Index], false));
            }

            spans.Add(new TextSpan(match.Groups["emphasized"].Value, true));
            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            spans.Add(new TextSpan(text[position..], false));
        }

        return spans;
    }
}
