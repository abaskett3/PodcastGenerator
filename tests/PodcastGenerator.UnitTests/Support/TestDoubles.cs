using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Domain.Security;

namespace PodcastGenerator.UnitTests.Support;

/// <summary>A key that is obviously fake. Tests search every output for it (AC-26).</summary>
internal static class TestKeys
{
    public const string Sentinel = "sk-test-SENTINEL-0123456789-not-a-real-key";
}

/// <summary>A file system held in memory, so the application logic is tested without touching a disk.</summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

    public List<string> DeletedFiles { get; } = [];

    /// <summary>When set, <see cref="DeleteFile"/> throws it, like a file that is locked.</summary>
    public Exception? DeleteFailure { get; set; }

    public bool FileExists(string path) => Files.ContainsKey(path);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public void CreateDirectory(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            Directories.Add(current);
        }
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Files.TryGetValue(path, out var text)
            ? Task.FromResult(text)
            : throw new FileNotFoundException("Not found.", path);
    }

    public Task<bool> TryWriteNewTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        if (Files.ContainsKey(path))
        {
            return Task.FromResult(false);
        }

        Files[path] = contents;
        return Task.FromResult(true);
    }

    public void MoveFile(string source, string destination)
    {
        if (Files.ContainsKey(destination))
        {
            throw new IOException("The destination exists.");
        }

        Files[destination] = Files[source];
        Files.Remove(source);
    }

    public void DeleteFile(string path)
    {
        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        DeletedFiles.Add(path);
        Files.Remove(path);
    }
}

internal sealed class FakeEnvironmentVariables : IEnvironmentVariables
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public string? Get(string name) => Values.TryGetValue(name, out var value) ? value : null;
}

internal sealed class FakeDefaultResources : IDefaultResources
{
    public FakeDefaultResources(string defaultStyle)
    {
        DefaultStyle = defaultStyle;
    }

    public string DefaultStyle { get; }
}

/// <summary>A clock that always says the same moment. The local time zone is UTC so the local date is the UTC date.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class FakeDelayer : IDelayer
{
    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingReporter : IRunReporter
{
    public List<(int Number, int Total)> Chunks { get; } = [];

    public List<string> Retries { get; } = [];

    public List<string> Warnings { get; } = [];

    /// <summary>Everything reported, in order, for tests that check the sequence.</summary>
    public List<string> Events { get; } = [];

    public void ChunkStarted(int number, int total)
    {
        Chunks.Add((number, total));
        Events.Add($"chunk {number} of {total}");
    }

    public void Retrying(int number, int total, int retry, int maxRetries, string reason)
    {
        Retries.Add(reason);
        Events.Add($"retry {retry} of {maxRetries} for chunk {number} of {total}");
    }

    public void Warning(string message)
    {
        Warnings.Add(message);
        Events.Add("warning");
    }
}

/// <summary>A speech client that never touches the network. It answers from a handler and records every call.</summary>
internal sealed class FakeSpeechClient : ISpeechClient
{
    public List<SpeechRequest> Requests { get; } = [];

    public List<string> Keys { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    /// <summary>Called with the zero-based call number and the request. Throw a <see cref="SpeechException"/> to fail.</summary>
    public Func<int, SpeechRequest, SpeechAudio> Handler { get; set; } = (_, _) => OneSecondOfSilence();

    public static SpeechAudio OneSecondOfSilence() => new(new byte[24000 * 2], 24000, 1);

    public Task<SpeechAudio> SynthesizeAsync(SpeechRequest request, ApiKey apiKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = Requests.Count;
        Requests.Add(request);
        Keys.Add(apiKey.Reveal());
        Tokens.Add(cancellationToken);
        return Task.FromResult(Handler(index, request));
    }
}

internal sealed class FakeAudioWorkspace : IAudioWorkspace
{
    private readonly InMemoryFileSystem _fileSystem;

    public FakeAudioWorkspace(InMemoryFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public List<SpeechAudio> Chunks { get; } = [];

    public string? WrittenPath { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>When set, <see cref="WriteMp3Async"/> creates the file and then throws, like an encoder that fails half way.</summary>
    public Exception? FailWhileWriting { get; set; }

    public Task AddChunkAsync(SpeechAudio audio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Chunks.Add(audio);
        return Task.CompletedTask;
    }

    public Task WriteMp3Async(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WrittenPath = path;
        _fileSystem.Files[path] = "fake-mp3";
        if (FailWhileWriting is not null)
        {
            throw FailWhileWriting;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeAudioWorkspaceFactory : IAudioWorkspaceFactory
{
    private readonly InMemoryFileSystem _fileSystem;

    public FakeAudioWorkspaceFactory(InMemoryFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public FakeAudioWorkspace? Last { get; private set; }

    public Exception? FailWhileWriting { get; set; }

    public IAudioWorkspace Create()
    {
        Last = new FakeAudioWorkspace(_fileSystem) { FailWhileWriting = FailWhileWriting };
        return Last;
    }
}
