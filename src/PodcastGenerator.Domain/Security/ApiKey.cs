namespace PodcastGenerator.Domain.Security;

/// <summary>The OpenRouter API key. The value is only readable through <see cref="Reveal"/>; every other way of turning
/// the object into text (<see cref="ToString"/>, record printing, logging, exception messages) yields a redacted
/// placeholder so the key cannot leak by accident.</summary>
public sealed class ApiKey
{
    private readonly string _value;

    public ApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An API key must not be empty.", nameof(value));
        }

        _value = value;
    }

    /// <summary>Returns the key. Call this only when building the HTTP authorization header.</summary>
    public string Reveal() => _value;

    public override string ToString() => "[redacted API key]";
}
