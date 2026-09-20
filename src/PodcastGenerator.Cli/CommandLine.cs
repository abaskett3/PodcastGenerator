namespace PodcastGenerator.Cli;

/// <summary>What the command line asks for.</summary>
public abstract record CliCommand;

public sealed record RunCommand(string ScriptPath, string? OutputPath) : CliCommand;

public sealed record HelpCommand : CliCommand;

public sealed record VersionCommand : CliCommand;

/// <summary>A usage error: too few or too many arguments, or an unknown option.</summary>
public sealed record UsageError(string Message) : CliCommand;

/// <summary>Parses <c>PodcastGenerator &lt;scriptPath&gt; [outputPath]</c>, <c>--help</c> and <c>--version</c> (AC-16, AC-17, D-18).</summary>
public static class CommandLine
{
    public const string Usage =
        "Usage: PodcastGenerator <scriptPath> [outputPath]\n" +
        "       PodcastGenerator --help\n" +
        "       PodcastGenerator --version\n" +
        "\n" +
        "  <scriptPath>  A podcast script: an existing .txt file.\n" +
        "  [outputPath]  Optional. An .mp3 file path, or an existing directory to write into.\n" +
        "                Default: PodcastGenerator/Podcast-MM-DD-YYYY.mp3 in your user profile folder.";

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
