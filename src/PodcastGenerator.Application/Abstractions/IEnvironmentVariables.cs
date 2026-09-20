namespace PodcastGenerator.Application.Abstractions;

public interface IEnvironmentVariables
{
    /// <summary>Returns the value of an environment variable, or <see langword="null"/> when it is not set.</summary>
    string? Get(string name);
}
