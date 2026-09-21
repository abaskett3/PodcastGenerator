using System.Text;

namespace PodcastGenerator.Application.Runtime;

/// <summary>Changes the text of a <c>.env</c> style file without disturbing the lines it does not touch. The reading side
/// is <see cref="EnvFileParser"/>; a line is recognised the same way (trimmed, <c>#</c> lines and blank lines skipped, the
/// key is the text before the first <c>=</c>).</summary>
public static class EnvFileEditor
{
    /// <summary>Sets <paramref name="key"/> to <paramref name="value"/> and keeps the key unique (AC-3, user decision, fix
    /// round 1): every line for that key, matched without regard to case, is removed and one new <c>KEY=VALUE</c> line,
    /// with the key spelled as given, is added at the end. Every other line, and its line ending, is kept exactly as it
    /// is. The new line uses the file's first line ending (taken before any line is removed), or the platform's for a
    /// file with none.</summary>
    public static string SetValue(string contents, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var lines = Split(contents);
        var newline = LineEndingOf(lines);
        lines.RemoveAll(line => IsLineFor(line.Text, key));
        AddLine(lines, key, value, newline);
        return Join(lines);
    }

    /// <summary>Adds <c>KEY=VALUE</c> as a new last line and changes nothing else, even when the key already has a line
    /// (AC-10).</summary>
    public static string AppendValue(string contents, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var lines = Split(contents);
        AddLine(lines, key, value, LineEndingOf(lines));
        return Join(lines);
    }

    /// <summary>The file's own line ending (the first one found); a file with none yet gets the platform's.</summary>
    private static string LineEndingOf(List<Line> lines) =>
        lines.Select(line => line.Ending).FirstOrDefault(ending => ending.Length > 0) ?? Environment.NewLine;

    private static void AddLine(List<Line> lines, string key, string value, string newline)
    {
        if (lines.Count > 0 && lines[^1].Ending.Length == 0)
        {
            lines[^1] = lines[^1] with { Ending = newline };
        }

        lines.Add(new Line($"{key}={value}", newline));
    }

    private static bool IsLineFor(string text, string key)
    {
        var line = text.Trim();
        if (line.Length == 0 || line[0] == '#')
        {
            return false;
        }

        var separator = line.IndexOf('=');
        return separator > 0 && string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Splits into lines that keep their own ending (<c>\r\n</c>, <c>\n</c> or <c>\r</c>; the last line may have
    /// none). A leading byte order mark is dropped, as <see cref="EnvFileParser"/> does.</summary>
    private static List<Line> Split(string contents)
    {
        var text = contents.TrimStart('﻿');
        var lines = new List<Line>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character != '\r' && character != '\n')
            {
                continue;
            }

            var endingLength = character == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            lines.Add(new Line(text[start..index], text.Substring(index, endingLength)));
            index += endingLength - 1;
            start = index + 1;
        }

        if (start < text.Length)
        {
            lines.Add(new Line(text[start..], string.Empty));
        }

        return lines;
    }

    private static string Join(List<Line> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line.Text).Append(line.Ending);
        }

        return builder.ToString();
    }

    private sealed record Line(string Text, string Ending);
}
