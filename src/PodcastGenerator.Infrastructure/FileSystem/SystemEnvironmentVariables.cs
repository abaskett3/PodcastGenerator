using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Infrastructure.FileSystem;

public sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}
