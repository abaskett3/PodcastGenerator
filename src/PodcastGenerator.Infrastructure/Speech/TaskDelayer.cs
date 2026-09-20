using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Infrastructure.Speech;

public sealed class TaskDelayer : IDelayer
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
