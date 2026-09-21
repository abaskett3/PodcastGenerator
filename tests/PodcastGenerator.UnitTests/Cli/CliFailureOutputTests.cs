using System.Text.RegularExpressions;
using PodcastGenerator.Cli;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Cli;

/// <summary>The failure output of a command line that gets past the config check: exit code, standard output and standard error,
/// through the real <see cref="CliRunner"/>, the real services and an in-memory profile. These used to run as a child process
/// (AC-15, AC-18 to AC-20, AC-13). They cannot any more: every command that generates a podcast now checks the config folder and
/// key file in the real user profile first (cli-set-config AC-6), and no test may read the user's key file (AC-5). The
/// assertions are the ones the process tests made.</summary>
public class CliFailureOutputTests
{
    private static async Task<(int Code, string Out, string Error)> RunAsync(ServiceFixture fixture, string script, string? output = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await fixture.CreateRunner().RunAsync(new RunCommand(script, output), stdout, stderr, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string Write(ServiceFixture fixture, string name, string text)
    {
        var path = Path.Combine(ServiceFixture.UserProfile, "scripts", name);
        fixture.FileSystem.Files[path] = text;
        return path;
    }

    // AC-18
    [Fact]
    public async Task A_missing_script_names_the_path_and_exits_1()
    {
        var fixture = new ServiceFixture();
        var missing = Path.Combine(ServiceFixture.UserProfile, "scripts", "missing.txt");

        var (exitCode, output, error) = await RunAsync(fixture, missing);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(missing, error, StringComparison.Ordinal);
    }

    // AC-19
    [Theory]
    [InlineData("script.md")]
    [InlineData("script.docx")]
    public async Task A_script_that_is_not_txt_is_rejected_saying_only_txt_is_supported(string fileName)
    {
        var fixture = new ServiceFixture();
        var path = Write(fixture, fileName, "CECIL: Good evening.\n");

        var (exitCode, output, error) = await RunAsync(fixture, path);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(".txt", error, StringComparison.Ordinal);
    }

    // AC-13
    [Fact]
    public async Task An_output_path_that_is_not_mp3_names_the_required_extension_and_exits_1()
    {
        var fixture = new ServiceFixture();
        var script = Write(fixture, "episode.txt", "CECIL: Good evening.\n");
        var wav = Path.Combine(ServiceFixture.UserProfile, "out", "out.wav");

        var (exitCode, output, error) = await RunAsync(fixture, script, wav);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(".mp3", error, StringComparison.Ordinal);
        Assert.False(fixture.FileSystem.FileExists(wav));
    }

    // AC-20
    [Fact]
    public async Task A_script_with_nothing_to_narrate_says_so_and_exits_1()
    {
        var fixture = new ServiceFixture();
        var script = Write(fixture, "cues.txt", "[MUSIC: THEME]\n\n(BEAT)\n\nEND\n");

        var (exitCode, output, error) = await RunAsync(fixture, script);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("nothing to narrate", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Speech.Requests);
    }

    // AC-15: every failure message goes to standard error, with no stack trace.
    [Fact]
    public async Task A_failure_message_is_one_line_of_text_and_no_stack_trace()
    {
        var fixture = new ServiceFixture();

        var (_, _, error) = await RunAsync(fixture, Path.Combine(ServiceFixture.UserProfile, "scripts", "missing.txt"));

        Assert.DoesNotMatch(new Regex(@"\bat [A-Za-z.]+\(", RegexOptions.None), error);
        Assert.StartsWith("Error:", error, StringComparison.Ordinal);
    }
}
