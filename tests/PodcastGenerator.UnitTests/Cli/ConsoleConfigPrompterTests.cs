using PodcastGenerator.Cli;
using PodcastGenerator.UnitTests.Support;

namespace PodcastGenerator.UnitTests.Cli;

/// <summary>The prompter over redirected input. The keystroke-by-keystroke path used at a real terminal
/// (<c>Console.ReadKey(intercept: true)</c>) needs a real console, so it is not exercised here; no test may need an interactive
/// console (cli-set-config, Constraints).</summary>
public class ConsoleConfigPrompterTests
{
    [Fact]
    public async Task It_shows_a_label_with_the_key_name_on_the_error_writer_and_returns_the_line_read()
    {
        var error = new StringWriter();
        var prompter = new ConsoleConfigPrompter(new StringReader("typed value\n"), error, hideTyping: false);

        var value = await prompter.ReadValueAsync("OPENROUTER_API_KEY", CancellationToken.None);

        Assert.Equal("typed value", value);
        Assert.Equal("OPENROUTER_API_KEY: ", error.ToString());
    }

    // cli-set-config AC-13: what is read is never written back.
    [Fact]
    public async Task What_is_read_is_never_written_to_the_error_writer()
    {
        var error = new StringWriter();
        var prompter = new ConsoleConfigPrompter(new StringReader(TestKeys.Sentinel + "\n"), error, hideTyping: false);

        await prompter.ReadValueAsync("OPENROUTER_API_KEY", CancellationToken.None);

        Assert.DoesNotContain(TestKeys.Sentinel, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_call_reads_the_next_line()
    {
        var prompter = new ConsoleConfigPrompter(new StringReader("one\ntwo\r\n"), new StringWriter(), hideTyping: false);

        Assert.Equal("one", await prompter.ReadValueAsync("K", CancellationToken.None));
        Assert.Equal("two", await prompter.ReadValueAsync("K", CancellationToken.None));
    }

    [Fact]
    public async Task Input_that_has_ended_gives_null_so_the_caller_can_stop()
    {
        var prompter = new ConsoleConfigPrompter(new StringReader(string.Empty), new StringWriter(), hideTyping: false);

        Assert.Null(await prompter.ReadValueAsync("K", CancellationToken.None));
    }

    [Fact]
    public async Task A_blank_line_is_returned_as_typed_so_the_caller_can_reject_it()
    {
        var prompter = new ConsoleConfigPrompter(new StringReader("\n"), new StringWriter(), hideTyping: false);

        Assert.Equal(string.Empty, await prompter.ReadValueAsync("K", CancellationToken.None));
    }

    [Fact]
    public void Tell_writes_the_message_and_a_line_ending_to_the_error_writer()
    {
        var error = new StringWriter();

        new ConsoleConfigPrompter(new StringReader(string.Empty), error, hideTyping: false).Tell("Invalid input");

        Assert.Equal("Invalid input" + Environment.NewLine, error.ToString());
    }
}
