using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Cli;
using PodcastGenerator.Infrastructure;

var command = CommandLine.Parse(args);
switch (command)
{
    case HelpCommand:
        Console.Out.WriteLine(CommandLine.Usage);
        return 0;
    case VersionCommand:
        Console.Out.WriteLine(VersionInfo.Get(typeof(CommandLine).Assembly));
        return 0;
    case UsageError usageError:
        Console.Error.WriteLine($"Error: {usageError.Message}");
        Console.Error.WriteLine(CommandLine.Usage);
        return 1;
    case SetConfigCommand setConfig:
        using (var cancellation = new CancellationTokenSource())
        {
            CancelOnCtrlC(cancellation);
            await using var provider = BuildProvider();
            return await provider.GetRequiredService<CliRunner>()
                .SetConfigAsync(setConfig, Console.Out, Console.Error, cancellation.Token)
                .ConfigureAwait(false);
        }

    case RunCommand run:
        using (var cancellation = new CancellationTokenSource())
        {
            CancelOnCtrlC(cancellation);
            await using var provider = BuildProvider();
            return await provider.GetRequiredService<CliRunner>()
                .RunAsync(run, Console.Out, Console.Error, cancellation.Token)
                .ConfigureAwait(false);
        }

    default:
        return 1;
}

static void CancelOnCtrlC(CancellationTokenSource cancellation) =>
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        // Ctrl+C cancels the run so the temporary and partial files are removed before the process ends.
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

static ServiceProvider BuildProvider()
{
    var services = new ServiceCollection();
    services.AddPodcastGeneratorApplication();
    services.AddPodcastGeneratorInfrastructure();
    services.AddSingleton<IRunReporter, ConsoleRunReporter>();
    services.AddSingleton<IConfigPrompter, ConsoleConfigPrompter>();
    services.AddSingleton<CliRunner>();

    return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}
