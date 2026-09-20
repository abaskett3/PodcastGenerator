namespace PodcastGenerator.Application.Abstractions;

/// <summary>Collects the audio of the chunks of one run without keeping all of it in memory, then writes the final MP3.
/// Disposing deletes every temporary file.</summary>
public interface IAudioWorkspace : IAsyncDisposable
{
    /// <summary>Stores the audio of the next chunk. Chunks are joined in the order they were added.</summary>
    Task AddChunkAsync(SpeechAudio audio, CancellationToken cancellationToken);

    /// <summary>Joins the stored chunks and writes one two-channel MP3 file with the mono voice centered.</summary>
    Task WriteMp3Async(string path, CancellationToken cancellationToken);
}

public interface IAudioWorkspaceFactory
{
    IAudioWorkspace Create();
}
