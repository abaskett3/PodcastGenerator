using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Runtime;

namespace PodcastGenerator.Cli;

/// <summary>Runs one command and turns the outcome into an exit code: 0 on success, 1 on any failure (AC-15).</summary>
public sealed class CliRunner
{
    private readonly IPodcastGenerationService _service;
    private readonly IConfigService _config;

    public CliRunner(IPodcastGenerationService service, IConfigService config)
    {
        _service = service;
        _config = config;
    }

    /// <summary>Generates a podcast. Before anything else it makes sure the config folder and file exist and every required
    /// config value is present, asking for a missing one (AC-6 to AC-11).</summary>
    public Task<int> RunAsync(RunCommand command, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return ExecuteAsync(
            error,
            cancellationToken,
            async () =>
            {
                await _config.EnsureRequiredAsync(cancellationToken).ConfigureAwait(false);
                var result = await _service.GenerateAsync(new GenerationRequest(command.ScriptPath, command.OutputPath), cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync(result.OutputPath).ConfigureAwait(false);
            });
    }

    /// <summary>Saves one config value and does nothing else (AC-1 to AC-5). The value is not printed back (AC-13).</summary>
    public Task<int> SetConfigAsync(SetConfigCommand command, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return ExecuteAsync(
            error,
            cancellationToken,
            async () =>
            {
                await _config.SetAsync(command.Key, command.Value, cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync($"Saved {command.Key}.").ConfigureAwait(false);
            });
    }

    private static async Task<int> ExecuteAsync(TextWriter error, CancellationToken cancellationToken, Func<Task> action)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action().ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Error: the run was cancelled. No output file was kept.").ConfigureAwait(false);
            return 1;
        }
        catch (InvalidInputException exception)
        {
            // Exactly the fixed message, with no "Error:" prefix (AC-4).
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
        catch (UserFacingException exception)
        {
            await error.WriteLineAsync($"Error: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (Exception exception)
        {
            // Only the exception type and message are printed; never the stack or any configuration.
            await error.WriteLineAsync($"Error: an unexpected failure occurred ({exception.GetType().Name}): {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
