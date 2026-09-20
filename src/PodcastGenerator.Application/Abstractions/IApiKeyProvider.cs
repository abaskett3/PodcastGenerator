using PodcastGenerator.Domain.Security;

namespace PodcastGenerator.Application.Abstractions;

public interface IApiKeyProvider
{
    /// <summary>Finds the OpenRouter API key: <c>PodcastGenerator.env</c> in the runtime folder first, then the
    /// <c>OPENROUTER_API_KEY</c> environment variable. Returns <see langword="null"/> when there is none.</summary>
    Task<ApiKey?> FindAsync(CancellationToken cancellationToken);
}
