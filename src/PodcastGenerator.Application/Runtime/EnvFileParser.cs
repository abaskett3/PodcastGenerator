namespace PodcastGenerator.Application.Runtime;

/// <summary>Reads <c>KEY=value</c> lines from a <c>.env</c> style file (AC-25). Blank lines and lines starting with
/// <c>#</c> are ignored, a UTF-8 byte order mark and either line ending are accepted, spaces around the key and value
/// and one pair of matching quotes are removed, other keys are ignored and the last duplicate wins. Keys are matched
/// without regard to case (user decision, cli-set-config fix round 1), so <c>openrouter_api_key=x</c> satisfies a lookup
/// for <c>OPENROUTER_API_KEY</c>.</summary>
public static class EnvFileParser
{
    /// <summary>Returns the value of <paramref name="key"/> (compared without regard to case), or <see langword="null"/>
    /// when it is absent or empty.</summary>
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

            if (!string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = Unquote(line[(separator + 1)..].Trim());
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>What the parser makes of the text after the <c>=</c> once it is trimmed: one matching pair of quotes is
    /// removed. Also used by <see cref="ConfigInput"/> to refuse a value the parser would read back as empty.</summary>
    internal static string Unquote(string value)
    {
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
        {
            return value[1..^1].Trim();
        }

        return value;
    }
}
