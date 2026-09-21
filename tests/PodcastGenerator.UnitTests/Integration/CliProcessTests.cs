using System.Diagnostics;
using System.Runtime.InteropServices;
using PodcastGenerator.Cli;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Starts the real built <c>PodcastGenerator.dll</c> (the top-level <c>Program</c>) as a process and checks its exit
/// code and its standard output and error (AC-15, AC-16, AC-17, and cli-set-config AC-4).
///
/// Only paths that end before the tool touches the runtime folder or the key are used here: usage errors, help, version and a
/// rejected <c>--set-config</c>. Any other command line makes the process check the config folder and key file in the real
/// user profile first, and then read the script and call the real API, which no test may do (AC-5). The failures that come after
/// that check (a missing script, a wrong extension, an empty script) are tested in process by <c>CliFailureOutputTests</c>
/// against an in-memory profile. As a second guard the child process gets no <c>OPENROUTER_API_KEY</c> and a proxy that points at a closed local port, so an
/// accidental request could not leave the machine.</summary>
public sealed class CliProcessTests
{
    private static string CliDll()
    {
        var baseDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var framework = Path.GetFileName(baseDirectory);
        var configuration = Path.GetFileName(Path.GetDirectoryName(baseDirectory)!);
        var path = RepositoryFiles.Combine("src", "PodcastGenerator.Cli", "bin", configuration, framework, "PodcastGenerator.dll");
        Assert.True(File.Exists(path), $"The built CLI was not found at {path}. Build the solution first.");
        return path;
    }

    private static string DotnetHost()
    {
        var fromSdk = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromSdk) && File.Exists(fromSdk))
        {
            return fromSdk;
        }

        // <root>/shared/Microsoft.NETCore.App/<version>/ -> <root>/dotnet
        var runtimeDirectory = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtimeDirectory)));
        var candidate = root is null ? null : Path.Combine(root, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet");
        return candidate is not null && File.Exists(candidate) ? candidate : "dotnet";
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var start = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(CliDll());
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove("OPENROUTER_API_KEY");
        foreach (var name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY", "https_proxy", "http_proxy", "all_proxy" })
        {
            start.Environment[name] = "http://127.0.0.1:9";
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The CLI did not finish within 60 seconds.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    // AC-16
    [Fact]
    public void No_arguments_print_the_usage_to_standard_error_and_exit_1()
    {
        var (exitCode, output, error) = Run();

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("<scriptPath> [outputPath]", error, StringComparison.Ordinal);
    }

    [Fact]
    public void More_than_two_arguments_print_the_usage_and_exit_1()
    {
        var (exitCode, output, error) = Run("a.txt", "b.mp3", "c");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("<scriptPath> [outputPath]", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_option_prints_the_usage_and_exits_1()
    {
        var (exitCode, output, error) = Run("--frobnicate");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--frobnicate", error, StringComparison.Ordinal);
        Assert.Contains("<scriptPath> [outputPath]", error, StringComparison.Ordinal);
    }

    // AC-17
    [Fact]
    public void Help_prints_the_usage_to_standard_output_and_exits_0()
    {
        var (exitCode, output, error) = Run("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("<scriptPath> [outputPath]", output, StringComparison.Ordinal);
        Assert.Empty(error);
    }

    [Fact]
    public void Version_prints_only_the_version_and_exits_0()
    {
        var (exitCode, output, error) = Run("--version");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        var version = output.Trim();
        Assert.Equal(VersionInfo.Get(typeof(CommandLine).Assembly), version);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", version);
        Assert.DoesNotContain("+", version, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", output.TrimEnd('\r', '\n'), StringComparison.Ordinal);
    }

    // cli-set-config AC-4: a key or value that is not usable is rejected with exactly "Invalid input" before any file is
    // touched, so these are safe to run against the real built program.
    [Theory]
    [InlineData("", "value")]
    [InlineData("   ", "value")]
    [InlineData("MY KEY", "value")]
    [InlineData("MY_KEY", "")]
    [InlineData("MY_KEY", "   ")]
    public void Set_config_with_an_unusable_key_or_value_prints_Invalid_input_and_exits_1(string key, string value)
    {
        var (exitCode, output, error) = Run("--set-config", key, value);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal("Invalid input", error.TrimEnd('\r', '\n'));
    }

    [Fact]
    public void Set_config_without_a_value_is_a_usage_error_and_exits_1()
    {
        var (exitCode, output, error) = Run("--set-config", "MY_KEY");

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("--set-config", error, StringComparison.Ordinal);
        Assert.Contains("--set-config <KEY> <VALUE>", error, StringComparison.Ordinal);
    }

    // cli-set-config, design D-11: --set-config must be the first argument and take exactly two more. Both cases end as a
    // usage error before any config file is touched, so they are safe against the real built program.
    [Theory]
    [InlineData("script.txt", "--set-config", "MY_KEY")]
    [InlineData("--set-config", "MY_KEY", "value", "extra")]
    public void Set_config_in_the_wrong_position_or_with_too_many_arguments_is_a_usage_error_and_exits_1(params string[] args)
    {
        var (exitCode, output, error) = Run(args);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("Error:", error, StringComparison.Ordinal);
        Assert.Contains("--set-config <KEY> <VALUE>", error, StringComparison.Ordinal);
    }

    // cli-set-config AC-12: --help and --version win even next to --set-config, so the process exits 0 and prints the usage or
    // the version. The --set-config part here has an unusable key, so if it were ever run first nothing could be written.
    [Fact]
    public void Help_next_to_set_config_prints_the_usage_and_exits_0()
    {
        var (exitCode, output, error) = Run("--set-config", "", "value", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--set-config <KEY> <VALUE>", output, StringComparison.Ordinal);
        Assert.Empty(error);
    }

    [Fact]
    public void Version_next_to_set_config_prints_one_line_and_exits_0()
    {
        var (exitCode, output, error) = Run("--set-config", "", "value", "--version");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain("--set-config", output, StringComparison.Ordinal);
    }

    // cli-set-config AC-12: help lists the new command and, like version, does not touch the config folder (it would have
    // exited 0 with nothing on standard error).
    [Fact]
    public void Help_lists_the_set_config_command()
    {
        var (exitCode, output, _) = Run("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--set-config <KEY> <VALUE>", output, StringComparison.Ordinal);
    }
}
