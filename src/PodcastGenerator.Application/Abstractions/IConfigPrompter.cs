namespace PodcastGenerator.Application.Abstractions;

/// <summary>Talks to the person at the console when a required config value is missing. The CLI implements it over the
/// console; tests use a fake, so no test needs an interactive console.</summary>
public interface IConfigPrompter
{
    /// <summary>Shows a line of text. It is a message, never a config value the person typed.</summary>
    void Tell(string message);

    /// <summary>Asks for the value of <paramref name="keyName"/> and returns what was typed. What is typed is not shown on
    /// screen. Returns <see langword="null"/> when there is no console input at all (standard input is closed or has
    /// ended), so the caller can stop instead of asking again.</summary>
    Task<string?> ReadValueAsync(string keyName, CancellationToken cancellationToken);
}
