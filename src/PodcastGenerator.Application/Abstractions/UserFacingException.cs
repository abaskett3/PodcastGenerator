namespace PodcastGenerator.Application.Abstractions;

/// <summary>A failure whose message is written for the person running the tool and is safe to print. It never contains
/// the API key.</summary>
public class UserFacingException : Exception
{
    public UserFacingException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
