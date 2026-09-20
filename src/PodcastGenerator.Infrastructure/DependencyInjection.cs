using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Infrastructure.Audio;
using PodcastGenerator.Infrastructure.FileSystem;
using PodcastGenerator.Infrastructure.Speech;

namespace PodcastGenerator.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The longest a single speech request may take. Narrating a chunk of a couple of minutes of audio takes far
    /// less; this only stops a hung connection.</summary>
    public static readonly TimeSpan SpeechRequestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Registers the implementations of the Application interfaces.</summary>
    public static IServiceCollection AddPodcastGeneratorInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IEnvironmentVariables, SystemEnvironmentVariables>();
        services.AddSingleton<IRuntimePaths>(_ => RuntimePaths.ForCurrentUser());
        services.AddSingleton<IDefaultResources, EmbeddedDefaultResources>();
        services.AddSingleton<IDelayer, TaskDelayer>();
        services.AddSingleton<IAudioWorkspaceFactory, Mp3AudioWorkspaceFactory>();
        services.AddHttpClient<ISpeechClient, OpenRouterSpeechClient>(client =>
        {
            client.BaseAddress = OpenRouterSpeechClient.BaseAddress;
            client.Timeout = SpeechRequestTimeout;
        });
        return services;
    }
}
