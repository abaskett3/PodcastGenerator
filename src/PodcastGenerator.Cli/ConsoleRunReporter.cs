using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Cli;

/// <summary>Writes progress and warnings to standard error, so standard output holds only the result path (AC-51).</summary>
public sealed class ConsoleRunReporter : IRunReporter
{
    private readonly TextWriter _error;

    public ConsoleRunReporter()
        : this(Console.Error)
    {
    }

    public ConsoleRunReporter(TextWriter error)
    {
        _error = error;
    }

    public void ChunkStarted(int number, int total) => _error.WriteLine($"chunk {number} of {total}");

    public void Retrying(int number, int total, int retry, int maxRetries, string reason) =>
        _error.WriteLine($"chunk {number} of {total} failed ({reason}); retry {retry} of {maxRetries}");

    public void Warning(string message) => _error.WriteLine($"Warning: {message}");
}
