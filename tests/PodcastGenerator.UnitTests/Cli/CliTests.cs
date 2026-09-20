using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Cli;
using PodcastGenerator.Infrastructure;
using PodcastGenerator.Infrastructure.Speech;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Cli;

public class CommandLineTests
{
    // AC-16
    [Fact]
    public void No_arguments_is_a_usage_error()
    {
        var command = CommandLine.Parse([]);

        Assert.IsType<UsageError>(command);
    }

    [Fact]
    public void More_than_two_arguments_is_a_usage_error()
    {
        Assert.IsType<UsageError>(CommandLine.Parse(["a.txt", "b.mp3", "c"]));
    }

    [Theory]
    [InlineData("--frobnicate")]
    [InlineData("-x")]
    [InlineData("--out=foo.mp3")]
    public void An_unknown_option_is_a_usage_error_that_names_it(string option)
    {
        var command = Assert.IsType<UsageError>(CommandLine.Parse(["script.txt", option]));

        Assert.Contains(option, command.Message);
    }

    [Fact]
    public void The_usage_text_shows_the_arguments()
    {
        Assert.Contains("<scriptPath> [outputPath]", CommandLine.Usage);
    }

    [Fact]
    public void One_argument_is_the_script_path()
    {
        var command = Assert.IsType<RunCommand>(CommandLine.Parse(["script.txt"]));

        Assert.Equal("script.txt", command.ScriptPath);
        Assert.Null(command.OutputPath);
    }

    [Fact]
    public void Two_arguments_are_the_script_path_and_the_output_path()
    {
        var command = Assert.IsType<RunCommand>(CommandLine.Parse(["script.txt", "out.mp3"]));

        Assert.Equal("out.mp3", command.OutputPath);
    }

    // AC-17, D-18
    [Fact]
    public void Help_and_version_are_recognized()
    {
        Assert.IsType<HelpCommand>(CommandLine.Parse(["--help"]));
        Assert.IsType<VersionCommand>(CommandLine.Parse(["--version"]));
        Assert.IsType<HelpCommand>(CommandLine.Parse(["script.txt", "--help"]));
    }

    [Fact]
    public void A_lone_dash_is_treated_as_a_path()
    {
        Assert.IsType<RunCommand>(CommandLine.Parse(["-"]));
    }
}

public class VersionInfoTests
{
    // AC-17: the version printed is exactly the version the assembly was built with.
    [Fact]
    public void The_version_is_the_informational_version_without_build_metadata()
    {
        var assembly = BuildAssemblyWithInformationalVersion("1.2.3+0123abc");

        Assert.Equal("1.2.3", VersionInfo.Get(assembly));
    }

    [Fact]
    public void A_version_without_metadata_is_returned_unchanged()
    {
        Assert.Equal("10.20.30", VersionInfo.Get(BuildAssemblyWithInformationalVersion("10.20.30")));
    }

    [Fact]
    public void The_executables_version_follows_the_Version_build_property()
    {
        var attribute = typeof(CommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        Assert.NotNull(attribute);

        Assert.Equal(attribute.InformationalVersion.Split('+')[0], VersionInfo.Get(typeof(CommandLine).Assembly));
        Assert.DoesNotContain('+', attribute.InformationalVersion); // no commit hash is appended
    }

    private static Assembly BuildAssemblyWithInformationalVersion(string version)
    {
        var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
        return AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("VersionInfoTestAssembly" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run,
            [new CustomAttributeBuilder(constructor, [version])]);
    }
}

public class CliRunnerTests
{
    private sealed class StubService : IPodcastGenerationService
    {
        public Func<GenerationRequest, CancellationToken, Task<GenerationResult>> Behavior { get; set; } =
            (_, _) => Task.FromResult(new GenerationResult("/out/Podcast.mp3", 1));

        public GenerationRequest? Received { get; private set; }

        public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
        {
            Received = request;
            return Behavior(request, cancellationToken);
        }
    }

    private static async Task<(int Code, string Out, string Error)> RunAsync(StubService service, string script = "s.txt", string? output = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await new CliRunner(service).RunAsync(new RunCommand(script, output), stdout, stderr, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    // AC-7, AC-15
    [Fact]
    public async Task Success_prints_only_the_output_path_to_standard_output_and_returns_zero()
    {
        var service = new StubService();

        var (code, output, error) = await RunAsync(service, "script.txt", "out.mp3");

        Assert.Equal(0, code);
        Assert.Equal("/out/Podcast.mp3" + Environment.NewLine, output);
        Assert.Empty(error);
        Assert.Equal(new GenerationRequest("script.txt", "out.mp3"), service.Received);
    }

    [Fact]
    public async Task A_user_facing_failure_prints_its_message_to_standard_error_and_returns_one()
    {
        var service = new StubService { Behavior = (_, _) => throw new UserFacingException("The script file 'x.txt' does not exist.") };

        var (code, output, error) = await RunAsync(service);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Equal("Error: The script file 'x.txt' does not exist." + Environment.NewLine, error);
    }

    [Fact]
    public async Task Cancellation_returns_one_and_says_no_output_was_kept()
    {
        var service = new StubService { Behavior = (_, _) => throw new OperationCanceledException() };

        var (code, output, error) = await RunAsync(service);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains("cancelled", error);
    }

    [Fact]
    public async Task An_unexpected_failure_returns_one_with_a_message_and_no_stack_trace()
    {
        var service = new StubService { Behavior = (_, _) => throw new InvalidOperationException("boom") };

        var (code, output, error) = await RunAsync(service);

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Contains("InvalidOperationException", error);
        Assert.Contains("boom", error);
        Assert.DoesNotContain(" at ", error);
    }

    // AC-26: end to end through the real service with a failing fake speech client that echoes the key.
    [Fact]
    public async Task The_key_never_reaches_standard_output_or_standard_error_when_narration_fails()
    {
        var fixture = new ServiceFixture();
        fixture.Speech.Handler = (_, _) =>
            throw new SpeechException("no", 401, $"Bad key {TestKeys.Sentinel}", isTransient: false);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await new CliRunner(fixture.CreateService()).RunAsync(new RunCommand(fixture.ScriptPath, null), stdout, stderr, CancellationToken.None);

        Assert.Equal(1, code);
        Assert.DoesNotContain(TestKeys.Sentinel, stdout.ToString());
        Assert.DoesNotContain(TestKeys.Sentinel, stderr.ToString());
        Assert.Contains("HTTP 401", stderr.ToString());
    }

    [Fact]
    public async Task A_successful_run_prints_the_full_path_of_the_written_file()
    {
        var fixture = new ServiceFixture();
        var stdout = new StringWriter();

        var code = await new CliRunner(fixture.CreateService()).RunAsync(new RunCommand(fixture.ScriptPath, null), stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Equal(fixture.DefaultOutputPath + Environment.NewLine, stdout.ToString());
    }
}

public class ConsoleRunReporterTests
{
    // AC-51
    [Fact]
    public void Progress_and_warnings_go_to_the_error_writer_in_the_documented_form()
    {
        var error = new StringWriter();
        var reporter = new ConsoleRunReporter(error);

        reporter.ChunkStarted(2, 5);
        reporter.Warning("a paragraph was split");
        reporter.Retrying(2, 5, 1, 5, "HTTP 503: busy");

        var lines = error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("chunk 2 of 5", lines[0]);
        Assert.Equal("Warning: a paragraph was split", lines[1]);
        Assert.Equal("chunk 2 of 5 failed (HTTP 503: busy); retry 1 of 5", lines[2]);
    }
}

public class CompositionTests
{
    // AC-4: services come from dependency injection.
    [Fact]
    public void The_composition_root_resolves_every_service_with_scope_and_build_validation()
    {
        var services = new ServiceCollection();
        services.AddPodcastGeneratorApplication();
        services.AddPodcastGeneratorInfrastructure();
        services.AddSingleton<IRunReporter, ConsoleRunReporter>();
        services.AddSingleton<CliRunner>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<CliRunner>());
        Assert.NotNull(provider.GetRequiredService<IPodcastGenerationService>());
        Assert.IsType<OpenRouterSpeechClient>(provider.GetRequiredService<ISpeechClient>());
        Assert.NotNull(provider.GetRequiredService<IAudioWorkspaceFactory>());
    }
}
