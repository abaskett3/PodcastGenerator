using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Application.Runtime;

public interface IRuntimeInitializer
{
    /// <summary>Creates the runtime folder and any missing default resource. An existing resource is never overwritten
    /// (AC-22, D-13).</summary>
    Task EnsureAsync(CancellationToken cancellationToken);
}

public sealed class RuntimeInitializer : IRuntimeInitializer
{
    private readonly IFileSystem _fileSystem;
    private readonly IRuntimePaths _paths;
    private readonly IDefaultResources _defaults;

    public RuntimeInitializer(IFileSystem fileSystem, IRuntimePaths paths, IDefaultResources defaults)
    {
        _fileSystem = fileSystem;
        _paths = paths;
        _defaults = defaults;
    }

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        try
        {
            _fileSystem.CreateDirectory(_paths.RuntimeFolder);
            await _fileSystem.TryWriteNewTextAsync(_paths.StyleFilePath, _defaults.DefaultStyle, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UserFacingException($"The runtime folder '{_paths.RuntimeFolder}' could not be prepared: {exception.Message}", exception);
        }
    }
}
