using System.Text;
using PodcastGenerator.Domain.Scripts;

namespace PodcastGenerator.Application.Narration;

/// <summary>The paragraphs of one script segment, as the model will read them: text with inline tags.</summary>
public sealed record RenderedSegment(IReadOnlyList<string> Paragraphs);

/// <summary>Turns a parsed script into transcript paragraphs with inline tags.</summary>
public sealed class TranscriptRenderer
{
    private readonly TagVocabulary _tags;

    public TranscriptRenderer(TagVocabulary tags)
    {
        _tags = tags;
    }

    /// <summary>Renders every segment. A pause is carried to the start of the next paragraph that has text (D-9), so a
    /// pause never ends a chunk and is never separated from the words that follow it. A pause with nothing after it
    /// is dropped. Tags that end up next to each other are merged into one bracket, separated by commas (D-9).</summary>
    public IReadOnlyList<RenderedSegment> Render(Script script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var rendered = new List<RenderedSegment>();
        var pendingPauseTags = new List<string>();

        foreach (var segment in script.Segments)
        {
            var paragraphs = new List<string>();
            foreach (var block in segment.Blocks)
            {
                switch (block)
                {
                    case PauseBlock pause:
                        pendingPauseTags.Add(_tags.ForPause(pause.Duration));
                        break;
                    case SpeechBlock speech:
                        paragraphs.Add(RenderParagraph(speech, pendingPauseTags));
                        pendingPauseTags.Clear();
                        break;
                }
            }

            if (paragraphs.Count > 0)
            {
                rendered.Add(new RenderedSegment(paragraphs));
            }
        }

        return rendered;
    }

    private string RenderParagraph(SpeechBlock speech, IReadOnlyList<string> leadingTags)
    {
        // A part is either a tag or text; consecutive tags are merged when the paragraph is composed.
        var parts = new List<(bool IsTag, string Value)>();
        foreach (var tag in leadingTags)
        {
            parts.Add((true, tag));
        }

        if (speech.Direction is not null)
        {
            parts.Add((true, _tags.ForDirection(speech.Direction)));
        }

        foreach (var span in speech.Spans)
        {
            if (span.IsEmphasized)
            {
                parts.Add((true, _tags.Emphasis));
            }

            parts.Add((false, span.Text));
        }

        var builder = new StringBuilder();
        var index = 0;
        while (index < parts.Count)
        {
            if (parts[index].IsTag)
            {
                var group = new List<string>();
                while (index < parts.Count && parts[index].IsTag)
                {
                    group.Add(parts[index].Value);
                    index++;
                }

                if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1]))
                {
                    builder.Append(' ');
                }

                builder.Append('[').Append(string.Join(", ", group)).Append(']');
            }
            else
            {
                var text = parts[index].Value;
                if (builder.Length > 0 && builder[^1] == ']')
                {
                    // The tag stays directly in front of its text, separated by one space.
                    text = text.TrimStart();
                    if (text.Length > 0)
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(text);
                index++;
            }
        }

        return builder.ToString().Trim();
    }
}
