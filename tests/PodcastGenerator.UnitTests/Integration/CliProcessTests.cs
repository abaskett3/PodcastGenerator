using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using PodcastGenerator.Cli;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Integration;

/// <summary>Starts the real built <c>PodcastGenerator.dll</c> (the top-level <c>Program</c>) as a process and checks its exit
/// code and its standard output and error (AC-15, AC-16, AC-17, AC-13, AC-18 to AC-20).
///
/// Only failure paths that end before the tool touches the runtime folder or the key are used here. A valid script would make
/// the process read the real user's profile and key file and call the real API, which no test may do (AC-5). As a second
/// guard the child process gets no <c>OPENROUTER_API_KEY</c> and a proxy that points at a closed local port, so an
/// accidental request could not leave the machine.</summary>
public sealed class CliProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pg-proc-" + Guid.NewGuid().ToString("N"));

    public CliProcessTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

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

    private string Write(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
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

    // AC-18
    [Fact]
    public void A_missing_script_names_the_path_and_exits_1()
    {
        var missing = Path.Combine(_directory, "missing.txt");

        var (exitCode, output, error) = Run(missing);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(missing, error, StringComparison.Ordinal);
    }

    // AC-19
    [Theory]
    [InlineData("script.md")]
    [InlineData("script.docx")]
    public void A_script_that_is_not_txt_is_rejected_saying_only_txt_is_supported(string fileName)
    {
        var path = Write(fileName, "CECIL: Good evening.\n");

        var (exitCode, output, error) = Run(path);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(".txt", error, StringComparison.Ordinal);
    }

    // AC-13
    [Fact]
    public void An_output_path_that_is_not_mp3_names_the_required_extension_and_exits_1()
    {
        var script = Write("episode.txt", "CECIL: Good evening.\n");

        var (exitCode, output, error) = Run(script, Path.Combine(_directory, "out.wav"));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(".mp3", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_directory, "out.wav")));
    }

    // AC-20
    [Fact]
    public void A_script_with_nothing_to_narrate_says_so_and_exits_1()
    {
        var script = Write("cues.txt", "[MUSIC: THEME]\n\n(BEAT)\n\nEND\n");

        var (exitCode, output, error) = Run(script);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("nothing to narrate", error, StringComparison.OrdinalIgnoreCase);
    }

    // AC-15: every failure message goes to standard error, with no stack trace.
    [Fact]
    public void A_failure_message_is_one_line_of_text_and_no_stack_trace()
    {
        var (_, _, error) = Run(Path.Combine(_directory, "missing.txt"));

        Assert.DoesNotMatch(new Regex(@"\bat [A-Za-z.]+\(", RegexOptions.None), error);
        Assert.StartsWith("Error:", error, StringComparison.Ordinal);
    }
}
