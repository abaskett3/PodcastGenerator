namespace PodcastGenerator.Application.Runtime;

/// <summary>What counts as a usable config key or value, for <c>--set-config</c> and for the interactive prompt (AC-4, AC-9).</summary>
public static class ConfigInput
{
    /// <summary>The message for a rejected key or value, fixed by the user and used verbatim.</summary>
    public const string InvalidInputMessage = "Invalid input";

    /// <summary>A key must not be empty or blank and must not contain any whitespace (AC-4). It must also not contain
    /// <c>=</c> or start with <c>#</c>: the file format reads the key as the text before the first <c>=</c> and treats a
    /// line that starts with <c>#</c> as a comment, so such a key could not be read back.</summary>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key)
        && key[0] != '#'
        && !key.Contains('=')
        && !key.Any(char.IsWhiteSpace);

    /// <summary>A value must not be empty, blank or whitespace-only (AC-4, AC-9). It must also stay on one line: a line
    /// break inside the value would write a second line into the file.</summary>
    public static bool IsValidValue(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.AsSpan().IndexOfAny('\r', '\n') < 0;
}
