using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Domain.Security;

namespace PodcastGenerator.Application.Runtime;

/// <summary>Finds the API key: the <c>PodcastGenerator.env</c> file wins, then the <c>OPENROUTER_API_KEY</c> environment
/// variable (AC-23). .NET user-secrets and every other file are not read (AC-27).</summary>
public sealed class ApiKeyProvider : IApiKeyProvider
{
    /// <summary>The name of the key in the key file and of the environment variable.</summary>
    public const string KeyName = "OPENROUTER_API_KEY";

    private readonly IFileSystem _fileSystem;
    private readonly IRuntimePaths _paths;
    private readonly IEnvironmentVariables _environment;

    public ApiKeyProvider(IFileSystem fileSystem, IRuntimePaths paths, IEnvironmentVariables environment)
    {
        _fileSystem = fileSystem;
        _paths = paths;
        _environment = environment;
    }

    public async Task<ApiKey?> FindAsync(CancellationToken cancellationToken)
    {
        if (_fileSystem.FileExists(_paths.KeyFilePath))
        {
            string contents;
            try
            {
                contents = await _fileSystem.ReadAllTextAsync(_paths.KeyFilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The message names the file only; it never includes file contents.
                throw new UserFacingException($"The key file '{_paths.KeyFilePath}' could not be read: {exception.GetType().Name}.", exception);
            }

            var fromFile = EnvFileParser.GetValue(contents, KeyName);
            if (fromFile is not null)
            {
                return new ApiKey(fromFile);
            }
        }

        var fromEnvironment = _environment.Get(KeyName);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : new ApiKey(fromEnvironment.Trim());
    }
}
