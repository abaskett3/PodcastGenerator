using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Narration;
using PodcastGenerator.Application.Runtime;
using PodcastGenerator.Application.Scripts;

namespace PodcastGenerator.Application;

public static class DependencyInjection
{
    /// <summary>Registers the application services. Infrastructure adds the implementations of the interfaces they use.</summary>
    public static IServiceCollection AddPodcastGeneratorApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TagVocabulary>();
        services.AddSingleton<TranscriptRenderer>();
        services.AddSingleton<ChunkPlanner>();
        services.AddSingleton<OutputPathResolver>();
        services.AddSingleton<IScriptParser, ScriptParser>();
        services.AddSingleton<IRuntimeInitializer, RuntimeInitializer>();
        services.AddSingleton<IStyleProvider, StyleProvider>();
        services.AddSingleton<IApiKeyProvider, ApiKeyProvider>();
        services.AddSingleton<IPodcastGenerationService, PodcastGenerationService>();
        return services;
    }
}
