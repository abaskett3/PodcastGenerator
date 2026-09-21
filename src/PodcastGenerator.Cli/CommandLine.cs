namespace PodcastGenerator.Cli;

/// <summary>What the command line asks for.</summary>
public abstract record CliCommand;

public sealed record RunCommand(string ScriptPath, string? OutputPath) : CliCommand;

/// <summary><c>--set-config &lt;KEY&gt; &lt;VALUE&gt;</c>. The value can be a secret, so its text is kept out of
/// <c>ToString</c>, which a debugger or a log line might print.</summary>
public sealed record SetConfigCommand(string Key, string Value) : CliCommand
{
    public override string ToString() => $"{nameof(SetConfigCommand)} {{ Key = {Key}, Value = [hidden] }}";
}

public sealed record HelpCommand : CliCommand;

public sealed record VersionCommand : CliCommand;

/// <summary>A usage error: too few or too many arguments, or an unknown option.</summary>
public sealed record UsageError(string Message) : CliCommand;

/// <summary>Parses <c>PodcastGenerator &lt;scriptPath&gt; [outputPath]</c>, <c>--set-config &lt;KEY&gt; &lt;VALUE&gt;</c>,
/// <c>--help</c> and <c>--version</c> (AC-16, AC-17, D-18).</summary>
public static class CommandLine
{
    public const string SetConfigOption = "--set-config";

    public const string Usage =
        "Usage: PodcastGenerator <scriptPath> [outputPath]\n" +
        "       PodcastGenerator --set-config <KEY> <VALUE>\n" +
        "       PodcastGenerator --help\n" +
        "       PodcastGenerator --version\n" +
        "\n" +
        "  <scriptPath>  A podcast script: an existing .txt file.\n" +
        "  [outputPath]  Optional. An .mp3 file path, or an existing directory to write into.\n" +
        "                Default: PodcastGenerator/Podcast-MM-DD-YYYY.mp3 in your user profile folder.\n" +
        "  --set-config  Saves a config value, such as OPENROUTER_API_KEY, in PodcastGenerator.env in\n" +
        "                the config folder (.config/PodcastGenerator in your user profile folder).\n" +
        "                <KEY> has no spaces; <VALUE> is not blank. The file is created if it is missing.";

    public static CliCommand Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains("--help", StringComparer.Ordinal))
        {
            return new HelpCommand();
        }

        if (args.Contains("--version", StringComparer.Ordinal))
        {
            return new VersionCommand();
        }

        if (args.Count > 0 && string.Equals(args[0], SetConfigOption, StringComparison.Ordinal))
        {
            // The two arguments after the option are taken as the key and the value as given, even a value that starts with
            // a dash. A key or value that is not usable is rejected later with "Invalid input", not here.
            return args.Count == 3
                ? new SetConfigCommand(args[1], args[2])
                : new UsageError($"{SetConfigOption} needs exactly two arguments, <KEY> and <VALUE>, but {args.Count - 1} were given.");
        }

        if (args.Contains(SetConfigOption, StringComparer.Ordinal))
        {
            return new UsageError($"{SetConfigOption} must be the first argument.");
        }

        var unknownOption = args.FirstOrDefault(argument => argument.StartsWith('-') && argument.Length > 1);
        if (unknownOption is not null)
        {
            return new UsageError($"Unknown option '{unknownOption}'.");
        }

        return args.Count switch
        {
            0 => new UsageError("No script file was given."),
            1 => new RunCommand(args[0], null),
            2 => new RunCommand(args[0], args[1]),
            _ => new UsageError($"Expected at most two arguments, but {args.Count} were given."),
        };
    }
}
