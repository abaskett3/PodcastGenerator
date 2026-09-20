using PodcastGenerator.Domain.Narration;

namespace PodcastGenerator.Application.Narration;

/// <summary>Splits a rendered script into chunks (D-11): at segment boundaries first, then at paragraph boundaries, and
/// only inside a paragraph at a sentence end. Tags are already part of the paragraph text, so a tag is never separated from
/// its text: a tag at the start of a sentence goes with that sentence.</summary>
public sealed class ChunkPlanner
{
    private const string ParagraphSeparator = "\n\n";
    private const string SentenceSeparator = " ";

    public ChunkPlan Plan(IReadOnlyList<RenderedSegment> segments, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);

        var warnings = new List<string>();
        var atoms = new List<List<string>>();

        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var paragraphs = segments[segmentIndex].Paragraphs;
            if (Length(paragraphs) <= maxCharacters)
            {
                atoms.Add(paragraphs.ToList());
                continue;
            }

            // The segment is larger than one chunk: its paragraphs become the units.
            for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
            {
                var paragraph = paragraphs[paragraphIndex];
                if (paragraph.Length <= maxCharacters)
                {
                    atoms.Add([paragraph]);
                    continue;
                }

                var pieces = SplitAtSentences(paragraph, maxCharacters, out var hasOversizedSentence);
                warnings.Add(Describe(segmentIndex + 1, paragraphIndex + 1, paragraph, maxCharacters, hasOversizedSentence));
                foreach (var piece in pieces)
                {
                    atoms.Add([piece]);
                }
            }
        }

        var chunks = new List<NarrationChunk>();
        var current = new List<string>();
        var currentLength = 0;

        void Flush()
        {
            if (current.Count > 0)
            {
                chunks.Add(new NarrationChunk(chunks.Count + 1, string.Join(ParagraphSeparator, current)));
                current.Clear();
                currentLength = 0;
            }
        }

        foreach (var atom in atoms)
        {
            var atomLength = Length(atom);
            var combined = current.Count == 0 ? atomLength : currentLength + ParagraphSeparator.Length + atomLength;
            if (current.Count > 0 && combined > maxCharacters)
            {
                Flush();
                combined = atomLength;
            }

            current.AddRange(atom);
            currentLength = combined;
        }

        Flush();
        return new ChunkPlan(chunks, warnings);
    }

    private static List<string> SplitAtSentences(string paragraph, int maxCharacters, out bool hasOversizedSentence)
    {
        hasOversizedSentence = false;
        var pieces = new List<string>();
        var current = string.Empty;

        foreach (var sentence in SentenceSplitter.Split(paragraph))
        {
            if (sentence.Length > maxCharacters)
            {
                hasOversizedSentence = true;
            }

            if (current.Length == 0)
            {
                current = sentence;
            }
            else if (current.Length + SentenceSeparator.Length + sentence.Length <= maxCharacters)
            {
                current = string.Concat(current, SentenceSeparator, sentence);
            }
            else
            {
                pieces.Add(current);
                current = sentence;
            }
        }

        if (current.Length > 0)
        {
            pieces.Add(current);
        }

        return pieces;
    }

    private static int Length(IReadOnlyList<string> paragraphs) =>
        paragraphs.Sum(paragraph => paragraph.Length) + (Math.Max(paragraphs.Count - 1, 0) * ParagraphSeparator.Length);

    private static string Describe(int segment, int paragraph, string text, int maxCharacters, bool hasOversizedSentence)
    {
        var start = text.Length <= 40 ? text : string.Concat(text.AsSpan(0, 40), "...");
        var message =
            $"Segment {segment}, paragraph {paragraph} (\"{start}\") is longer than the chunk size of {maxCharacters} characters " +
            "and was split at sentence ends.";
        return hasOversizedSentence
            ? message + " One sentence is longer than the chunk size and was sent whole."
            : message;
    }
}
