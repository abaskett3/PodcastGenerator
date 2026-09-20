using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Infrastructure.Audio;
using PodcastGenerator.Infrastructure.FileSystem;
using PodcastGenerator.Infrastructure.Speech;

namespace PodcastGenerator.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The longest a single speech attempt may take, from sending the request to the last byte of audio. Narrating a
    /// chunk of a couple of minutes of audio takes far less (30 to 80 seconds was measured); this only stops a hung connection
    /// or a stalled body. It is applied by <see cref="OpenRouterSpeechClient"/> and is retried like any other timeout.</summary>
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
        services.AddSingleton(new SpeechClientOptions(SpeechRequestTimeout));
        services.AddHttpClient<ISpeechClient, OpenRouterSpeechClient>(client =>
        {
            client.BaseAddress = OpenRouterSpeechClient.BaseAddress;
            // HttpClient.Timeout does not cover reading the body of a ResponseHeadersRead response, so the client applies
            // SpeechClientOptions.RequestTimeout itself to the whole attempt. Leaving both would make two clocks race.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        return services;
    }
}
