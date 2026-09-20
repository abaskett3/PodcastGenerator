namespace PodcastGenerator.Application.Abstractions;

/// <summary>Waits between retries. A separate interface so tests do not really wait.</summary>
public interface IDelayer
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
