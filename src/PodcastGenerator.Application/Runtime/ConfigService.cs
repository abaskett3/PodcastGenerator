using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Application.Runtime;

/// <summary>The config values the app cannot run without. Only <c>OPENROUTER_API_KEY</c> today; the check in
/// <see cref="ConfigService"/> works for any number of names, so a later value is one more entry.</summary>
public sealed record RequiredConfig(IReadOnlyList<string> Keys);

public interface IConfigService
{
    /// <summary>Sets <paramref name="key"/> to <paramref name="value"/> in the key file, creating the runtime folder and the
    /// file first when they are missing, and updating the existing line for the key in place (AC-1 to AC-4). Nothing is
    /// touched when the key or the value is rejected.</summary>
    /// <exception cref="InvalidInputException">The key or the value is not usable.</exception>
    /// <exception cref="UserFacingException">The file could not be written.</exception>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);

    /// <summary>Creates the runtime folder and the key file when missing, then makes sure every required value is present in
    /// the file or the environment, asking the person for each one that is not (AC-6 to AC-10).</summary>
    /// <exception cref="UserFacingException">A required value is still missing: no console input, or five invalid entries.</exception>
    Task EnsureRequiredAsync(CancellationToken cancellationToken);
}

public sealed class ConfigService : IConfigService
{
    /// <summary>How many times the person may enter a value before the run fails (AC-9): five attempts in total.</summary>
    public const int MaxAttempts = 5;

    private readonly IFileSystem _fileSystem;
    private readonly IRuntimePaths _paths;
    private readonly IEnvironmentVariables _environment;
    private readonly IConfigPrompter _prompter;
    private readonly RequiredConfig _required;

    public ConfigService(
        IFileSystem fileSystem,
        IRuntimePaths paths,
        IEnvironmentVariables environment,
        IConfigPrompter prompter,
        RequiredConfig required)
    {
        _fileSystem = fileSystem;
        _paths = paths;
        _environment = environment;
        _prompter = prompter;
        _required = required;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (!ConfigInput.IsValidKey(key) || !ConfigInput.IsValidValue(value))
        {
            throw new InvalidInputException();
        }

        try
        {
            await EnsureFileAsync(cancellationToken).ConfigureAwait(false);
            var contents = await _fileSystem.ReadAllTextAsync(_paths.KeyFilePath, cancellationToken).ConfigureAwait(false);
            var updated = EnvFileEditor.SetValue(contents, key, value.Trim());
            await _fileSystem.ReplacePrivateTextAsync(_paths.KeyFilePath, updated, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw FileFailure(exception);
        }
    }

    public async Task EnsureRequiredAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureFileAsync(cancellationToken).ConfigureAwait(false);

            foreach (var key in _required.Keys)
            {
                if (!await IsPresentAsync(key, cancellationToken).ConfigureAwait(false))
                {
                    await AskForAsync(key, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw FileFailure(exception);
        }
    }

    /// <summary>Creates the runtime folder and an empty key file when either is missing (AC-1, AC-2, AC-6). An existing file
    /// is never touched.</summary>
    private async Task EnsureFileAsync(CancellationToken cancellationToken)
    {
        _fileSystem.CreateDirectory(_paths.RuntimeFolder);
        if (!_fileSystem.FileExists(_paths.KeyFilePath))
        {
            await _fileSystem.TryWriteNewPrivateTextAsync(_paths.KeyFilePath, string.Empty, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A value is present when the file has a non-empty value for it or the environment variable of the same name
    /// is non-empty (AC-7). This is the same rule <see cref="ApiKeyProvider"/> applies when it reads the key.</summary>
    private async Task<bool> IsPresentAsync(string key, CancellationToken cancellationToken)
    {
        var contents = await _fileSystem.ReadAllTextAsync(_paths.KeyFilePath, cancellationToken).ConfigureAwait(false);
        if (EnvFileParser.GetValue(contents, key) is not null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(_environment.Get(key));
    }

    private async Task AskForAsync(string key, CancellationToken cancellationToken)
    {
        _prompter.Tell($"{key} not found. Please set this config value to continue.");

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var entry = await _prompter.ReadValueAsync(key, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                throw new UserFacingException(NoConsoleInputMessage(key));
            }

            if (ConfigInput.IsValidValue(entry))
            {
                // Append only (AC-10): an existing line for the key, such as one with an empty value, is left alone. The
                // file parser lets the last line for a key win, so the new line is the one that is used.
                var contents = await _fileSystem.ReadAllTextAsync(_paths.KeyFilePath, cancellationToken).ConfigureAwait(false);
                var updated = EnvFileEditor.AppendValue(contents, key, entry.Trim());
                await _fileSystem.ReplacePrivateTextAsync(_paths.KeyFilePath, updated, cancellationToken).ConfigureAwait(false);
                return;
            }

            _prompter.Tell(ConfigInput.InvalidInputMessage);
        }

        throw new UserFacingException($"No valid value for {key} was entered after {MaxAttempts} attempts.");
    }

    private string NoConsoleInputMessage(string key) =>
        $"{key} was not found and there is no console input to ask for it. Set it with " +
        $"'PodcastGenerator --set-config {key} <value>' (it is saved as the line {key}=<value> in '{_paths.KeyFilePath}'), " +
        $"or set the {key} environment variable.";

    // The message names the file and the kind of failure only; it never includes file contents.
    private UserFacingException FileFailure(Exception exception) =>
        new($"The config file '{_paths.KeyFilePath}' could not be prepared or updated: {exception.GetType().Name}.", exception);
}
