namespace PodcastGenerator.Application.Abstractions;

/// <summary>A config key or value was rejected. The message is exactly <see cref="Runtime.ConfigInput.InvalidInputMessage"/>,
/// and it never contains what was rejected, because that may be a secret.</summary>
public sealed class InvalidInputException : UserFacingException
{
    public InvalidInputException()
        : base(Runtime.ConfigInput.InvalidInputMessage)
    {
    }
}
