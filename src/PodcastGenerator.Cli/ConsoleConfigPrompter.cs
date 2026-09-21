using System.Text;
using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Cli;

/// <summary>Asks for a missing config value on the console. Messages go to standard error, so standard output holds only
/// the result of a command. What the person types is never printed back.</summary>
public sealed class ConsoleConfigPrompter : IConfigPrompter
{
    private static readonly TimeSpan KeyPollInterval = TimeSpan.FromMilliseconds(25);

    private readonly TextReader _input;
    private readonly TextWriter _error;
    private readonly bool _hideTyping;

    /// <summary>Uses the real console. When standard input is a terminal the typed characters are not shown; when it is
    /// redirected (a pipe or a file) a line is read from it.</summary>
    public ConsoleConfigPrompter()
        : this(Console.In, Console.Error, hideTyping: !Console.IsInputRedirected)
    {
    }

    /// <param name="hideTyping">Read key by key from the terminal without showing the keys. <see cref="Console.ReadKey(bool)"/>
    /// needs a real terminal, so <see langword="false"/> reads a line from <paramref name="input"/> instead.</param>
    public ConsoleConfigPrompter(TextReader input, TextWriter error, bool hideTyping)
    {
        _input = input;
        _error = error;
        _hideTyping = hideTyping;
    }

    public void Tell(string message) => _error.WriteLine(message);

    public async Task<string?> ReadValueAsync(string keyName, CancellationToken cancellationToken)
    {
        await _error.WriteAsync($"{keyName}: ").ConfigureAwait(false);
        await _error.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (!_hideTyping)
        {
            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                await _error.WriteLineAsync().ConfigureAwait(false);
            }

            return line;
        }

        return await ReadHiddenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads keys until Enter without echoing them (<c>Console.ReadKey(intercept: true)</c>). It polls
    /// <c>Console.KeyAvailable</c> instead of blocking in <c>ReadKey</c>, so Ctrl+C, which cancels the token, ends the wait.</summary>
    private async Task<string> ReadHiddenAsync(CancellationToken cancellationToken)
    {
        var typed = new StringBuilder();
        while (true)
        {
            while (!Console.KeyAvailable)
            {
                await Task.Delay(KeyPollInterval, cancellationToken).ConfigureAwait(false);
            }

            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                await _error.WriteLineAsync().ConfigureAwait(false);
                return typed.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
    }
}
