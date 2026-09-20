namespace PodcastGenerator.Application.Abstractions;

/// <summary>Tells the person running the tool what is happening. The CLI writes these to standard error.</summary>
public interface IRunReporter
{
    /// <summary>Called before the request for chunk <paramref name="number"/> of <paramref name="total"/>.</summary>
    void ChunkStarted(int number, int total);

    /// <summary>Called when chunk <paramref name="number"/> failed with a failure that can pass and is about to be tried
    /// again. <paramref name="retry"/> counts from 1.</summary>
    void Retrying(int number, int total, int retry, int maxRetries, string reason);

    void Warning(string message);
}
