using GroovyCodecs.Mp3;
using GroovyCodecs.Types;
using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Infrastructure.Audio;

/// <summary>Stores each chunk's PCM in a temporary file, so the audio of a long script is never all in memory (D-15), and
/// encodes them, in order, into one two-channel MP3 with the mono voice centered (AC-53). The encoder is
/// GroovyMp3, a pure managed port of LAME (LGPL-3.0); see the design doc, "Audio library".</summary>
public sealed class Mp3AudioWorkspace : IAudioWorkspace
{
    /// <summary>Constant bitrate of the MP3, in kilobits per second, for both channels together (D-14).</summary>
    public const int BitRateKbps = 96;

    private const int InputSecondsPerBlock = 1;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PodcastGenerator-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _chunkFiles = [];
    private int? _sampleRate;

    /// <summary>The temporary folder that holds the chunk files. It is deleted when the workspace is disposed.</summary>
    public string WorkingDirectory => _directory;

    public async Task AddChunkAsync(SpeechAudio audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.Channels != 1)
        {
            throw new NotSupportedException($"Only mono audio is supported, but a chunk has {audio.Channels} channels.");
        }

        if (audio.Data.Length == 0 || audio.Data.Length % 2 != 0)
        {
            throw new InvalidDataException($"A chunk's audio must be 16-bit PCM, but it has {audio.Data.Length} bytes.");
        }

        if (_sampleRate is { } known && known != audio.SampleRate)
        {
            throw new InvalidDataException($"All chunks must have the same sample rate ({known} Hz), but one has {audio.SampleRate} Hz.");
        }

        _sampleRate = audio.SampleRate;
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"chunk-{_chunkFiles.Count + 1:D5}.pcm");
        await File.WriteAllBytesAsync(path, audio.Data, cancellationToken).ConfigureAwait(false);
        _chunkFiles.Add(path);
    }

    public Task WriteMp3Async(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_chunkFiles.Count == 0 || _sampleRate is not { } sampleRate)
        {
            throw new InvalidOperationException("There is no audio to write.");
        }

        return Task.Run(() => Encode(path, sampleRate, cancellationToken), cancellationToken);
    }

    private void Encode(string outputPath, int sampleRate, CancellationToken cancellationToken)
    {
        // Source format: 16-bit little-endian stereo. Each mono sample is written to both channels, so the two
        // channels carry identical samples (the voice is centered, with no panning).
        var source = new AudioFormat
        {
            SampleRate = sampleRate,
            Channels = 2,
            BitsPerSample = 16,
            BlockAlign = 4,
            AverageBytesPerSecond = sampleRate * 4,
            BigEndian = false,
            IsFloatingPoint = false,
        };

        var encoder = new Mp3Encoder(source, BitRateKbps, Mp3Encoder.CHANNEL_MODE_STEREO, Mp3Encoder.QUALITY_MIDDLE, VBR: false);
        try
        {
            var monoBlock = new byte[sampleRate * InputSecondsPerBlock * 2];
            var stereoBlock = new byte[monoBlock.Length * 2];
            // LAME's documented worst case for the size of the encoded output: 1.25 * samples + 7200 bytes.
            var mp3Buffer = new byte[(int)(1.25 * sampleRate * InputSecondsPerBlock) + 7200 + 1024];

            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            foreach (var chunkFile in _chunkFiles)
            {
                using var input = new FileStream(chunkFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                int read;
                while ((read = ReadBlock(input, monoBlock)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DuplicateToStereo(monoBlock.AsSpan(0, read), stereoBlock);
                    var length = encoder.EncodeBuffer(stereoBlock, 0, read * 2, mp3Buffer);
                    output.Write(mp3Buffer, 0, length);
                }
            }

            var tail = encoder.EncodeFinish(mp3Buffer);
            output.Write(mp3Buffer, 0, tail);
        }
        finally
        {
            encoder.Close();
        }
    }

    /// <summary>Reads up to a whole block, always a whole number of 16-bit samples.</summary>
    private static int ReadBlock(Stream input, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = input.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total - (total % 2);
    }

    private static void DuplicateToStereo(ReadOnlySpan<byte> mono, Span<byte> stereo)
    {
        for (var sample = 0; sample < mono.Length; sample += 2)
        {
            var left = sample * 2;
            stereo[left] = mono[sample];
            stereo[left + 1] = mono[sample + 1];
            stereo[left + 2] = mono[sample];
            stereo[left + 3] = mono[sample + 1];
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort: a temporary file that cannot be removed must not hide the real result or error.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class Mp3AudioWorkspaceFactory : IAudioWorkspaceFactory
{
    public IAudioWorkspace Create() => new Mp3AudioWorkspace();
}
