using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Application.Generation;

namespace PodcastGenerator.Cli;

/// <summary>Runs one command and turns the outcome into an exit code: 0 on success, 1 on any failure (AC-15).</summary>
public sealed class CliRunner
{
    private readonly IPodcastGenerationService _service;

    public CliRunner(IPodcastGenerationService service)
    {
        _service = service;
    }

    public async Task<int> RunAsync(RunCommand command, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            var result = await _service.GenerateAsync(new GenerationRequest(command.ScriptPath, command.OutputPath), cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync(result.OutputPath).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Error: the run was cancelled. No output file was kept.").ConfigureAwait(false);
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
