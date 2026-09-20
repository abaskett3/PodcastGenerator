namespace PodcastGenerator.Application.Narration;

/// <summary>Splits text into sentences without ever splitting inside a bracketed tag.</summary>
internal static class SentenceSplitter
{
    public static IReadOnlyList<string> Split(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        var depth = 0;
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];
            if (character == '[')
            {
                depth++;
            }
            else if (character == ']' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0 && IsSentenceEnd(character))
            {
                // Include any closing quote or bracket that belongs to this sentence.
                var end = index + 1;
                while (end < text.Length && IsClosing(text[end]))
                {
                    end++;
                }

                if (end >= text.Length || char.IsWhiteSpace(text[end]))
                {
                    var sentence = text[start..end].Trim();
                    if (sentence.Length > 0)
                    {
                        sentences.Add(sentence);
                    }

                    while (end < text.Length && char.IsWhiteSpace(text[end]))
                    {
                        end++;
                    }

                    start = end;
                    index = end;
                    continue;
                }
            }

            index++;
        }

        if (start < text.Length)
        {
            var rest = text[start..].Trim();
            if (rest.Length > 0)
            {
                sentences.Add(rest);
            }
        }

        return sentences;
    }

    private static bool IsSentenceEnd(char character) => character is '.' or '!' or '?' or '…';

    private static bool IsClosing(char character) => character is '"' or '\'' or ')' or '”' or '’';
}
