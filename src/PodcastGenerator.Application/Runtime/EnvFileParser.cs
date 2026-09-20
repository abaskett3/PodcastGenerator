namespace PodcastGenerator.Application.Runtime;

/// <summary>Reads <c>KEY=value</c> lines from a <c>.env</c> style file (AC-25). Blank lines and lines starting with
/// <c>#</c> are ignored, a UTF-8 byte order mark and either line ending are accepted, spaces around the key and value
/// and one pair of matching quotes are removed, other keys are ignored and the last duplicate wins.</summary>
public static class EnvFileParser
{
    /// <summary>Returns the value of <paramref name="key"/>, or <see langword="null"/> when it is absent or empty.</summary>
    public static string? GetValue(string fileContents, string key)
    {
        ArgumentNullException.ThrowIfNull(fileContents);
        ArgumentException.ThrowIfNullOrEmpty(key);

        string? value = null;
        var lines = fileContents.TrimStart('﻿').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (!string.Equals(line[..separator].Trim(), key, StringComparison.Ordinal))
            {
                continue;
            }

            value = Unquote(line[(separator + 1)..].Trim());
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
        {
            return value[1..^1].Trim();
        }

        return value;
    }
}
