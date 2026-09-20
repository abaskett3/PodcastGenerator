using NLayer;
using PodcastGenerator.Application.Abstractions;
using PodcastGenerator.Infrastructure.Audio;

namespace PodcastGenerator.UnitTests.Infrastructure;

/// <summary>Encodes synthetic tones (test data made here, not API responses) and decodes the MP3 back to check AC-52 and
/// AC-53. Files go to a temporary directory that each test deletes.</summary>
public sealed class Mp3AudioWorkspaceTests : IDisposable
{
    private const int Rate = 24000;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pg-unit-" + Guid.NewGuid().ToString("N"));

    public Mp3AudioWorkspaceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static SpeechAudio Tone(double frequency, double seconds, int rate = Rate)
    {
        var samples = (int)(seconds * rate);
        var bytes = new byte[samples * 2];
        for (var index = 0; index < samples; index++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * frequency * index / rate) * 0.5 * short.MaxValue);
            bytes[index * 2] = (byte)(value & 0xFF);
            bytes[(index * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return new SpeechAudio(bytes, rate, 1);
    }

    private string OutputPath(string name = "out.mp3") => Path.Combine(_directory, name);

    private static (int SampleRate, int Channels, float[] Left, float[] Right) Decode(string path)
    {
        using var file = new MpegFile(path);
        var left = new List<float>();
        var right = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = file.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index + 1 < read; index += 2)
            {
                left.Add(buffer[index]);
                right.Add(buffer[index + 1]);
            }
        }

        return (file.SampleRate, file.Channels, left.ToArray(), right.ToArray());
    }

    private static int ZeroCrossings(float[] samples, double fromSeconds, double toSeconds)
    {
        var crossings = 0;
        var start = (int)(fromSeconds * Rate);
        var end = (int)(toSeconds * Rate);
        for (var index = start + 1; index < end; index++)
        {
            if ((samples[index - 1] < 0) != (samples[index] < 0))
            {
                crossings++;
            }
        }

        return crossings;
    }

    // AC-53
    [Fact]
    public async Task The_output_is_a_valid_two_channel_mp3_with_identical_channels()
    {
        await using var workspace = new Mp3AudioWorkspace();
        await workspace.AddChunkAsync(Tone(440, 1.5), CancellationToken.None);

        await workspace.WriteMp3Async(OutputPath(), CancellationToken.None);

        var (sampleRate, channels, left, right) = Decode(OutputPath());
        Assert.Equal(Rate, sampleRate);
        Assert.Equal(2, channels);
        Assert.NotEmpty(left);
        Assert.Equal(left.Length, right.Length);
        Assert.True(left.SequenceEqual(right), "The two channels must carry identical samples (centered, no panning).");
        Assert.Contains(left, sample => Math.Abs(sample) > 0.1f); // the decoded audio is not silence
    }

    // AC-52: no chunk missing, duplicated or reordered, and the duration is the sum within a stated tolerance.
    [Fact]
    public async Task Chunks_are_joined_in_order_and_the_duration_is_the_sum_within_the_encoder_padding()
    {
        await using var workspace = new Mp3AudioWorkspace();
        await workspace.AddChunkAsync(Tone(440, 1.0), CancellationToken.None);
        await workspace.AddChunkAsync(Tone(880, 1.0), CancellationToken.None);
        await workspace.AddChunkAsync(Tone(440, 1.0), CancellationToken.None);

        await workspace.WriteMp3Async(OutputPath(), CancellationToken.None);

        var (_, _, left, _) = Decode(OutputPath());
        var seconds = left.Length / (double)Rate;

        // Tolerance: an MPEG-2 Layer III frame at 24 kHz is 576 samples (24 ms). LAME's encoder delay (576 samples) plus the
        // decoder delay (529 samples) plus padding to a whole frame add at most about 70 ms. 100 ms is allowed.
        Assert.InRange(seconds, 3.0, 3.1);

        // A sine wave crosses zero twice per cycle, so in a 0.5 s window 440 Hz gives about 440 crossings and 880 Hz about 880.
        var first = ZeroCrossings(left, 0.3, 0.8);
        var second = ZeroCrossings(left, 1.3, 1.8);
        var third = ZeroCrossings(left, 2.3, 2.8);
        Assert.InRange(first, 400, 480);
        Assert.InRange(second, 840, 920);
        Assert.InRange(third, 400, 480);
    }

    [Fact]
    public async Task No_silence_is_added_between_chunks()
    {
        await using var workspace = new Mp3AudioWorkspace();
        await workspace.AddChunkAsync(Tone(440, 1.0), CancellationToken.None);
        await workspace.AddChunkAsync(Tone(440, 1.0), CancellationToken.None);

        await workspace.WriteMp3Async(OutputPath(), CancellationToken.None);

        var (_, _, left, _) = Decode(OutputPath());
        // A continuous tone has a zero crossing in every 100 ms window, including the one around the chunk boundary.
        for (var window = 0.2; window < 1.8; window += 0.1)
        {
            Assert.True(ZeroCrossings(left, window, window + 0.1) > 20, $"silence found near {window:F1} s");
        }
    }

    // D-14: constant bitrate suited to speech.
    [Fact]
    public async Task The_mp3_has_the_documented_constant_bitrate()
    {
        await using var workspace = new Mp3AudioWorkspace();
        await workspace.AddChunkAsync(Tone(440, 4.0), CancellationToken.None);

        await workspace.WriteMp3Async(OutputPath(), CancellationToken.None);

        var kilobitsPerSecond = new FileInfo(OutputPath()).Length * 8 / 4.0 / 1000;
        Assert.InRange(kilobitsPerSecond, Mp3AudioWorkspace.BitRateKbps * 0.95, Mp3AudioWorkspace.BitRateKbps * 1.05);
    }

    // D-15: chunk audio is stored in temporary files that are deleted afterwards.
    [Fact]
    public async Task Chunk_audio_is_kept_in_temporary_files_that_are_deleted_on_dispose()
    {
        var workspace = new Mp3AudioWorkspace();
        await workspace.AddChunkAsync(Tone(440, 0.5), CancellationToken.None);
        await workspace.AddChunkAsync(Tone(440, 0.5), CancellationToken.None);
        Assert.Equal(2, Directory.GetFiles(workspace.WorkingDirectory).Length);

        await workspace.DisposeAsync();

        Assert.False(Directory.Exists(workspace.WorkingDirectory));
    }

    [Fact]
    public async Task Disposing_a_workspace_that_never_stored_audio_is_harmless()
    {
        var workspace = new Mp3AudioWorkspace();

        await workspace.DisposeAsync();
        await workspace.DisposeAsync();

        Assert.False(Directory.Exists(workspace.WorkingDirectory));
    }

    [Fact]
    public async Task An_existing_output_file_is_never_overwritten()
    {
        await using var workspace = new Mp3AudioWorkspace();
        await workspace.AddChunkAsync(Tone(440, 0.5), CancellationToken.None);
        await File.WriteAllTextAsync(OutputPath(), "PRECIOUS");

        await Assert.ThrowsAsync<IOException>(() => workspace.WriteMp3Async(OutputPath(), CancellationToken.None));

        Assert.Equal("PRECIOUS", await File.ReadAllTextAsync(OutputPath()));
    }

    [Fact]
    public async Task Writing_with_no_chunks_is_an_error()
    {
        await using var workspace = new Mp3AudioWorkspace();

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.WriteMp3Async(OutputPath(), CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_encoding()
    {
        await using var workspace = new Mp3AudioWorkspace();
        await workspace.AddChunkAsync(Tone(440, 2.0), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.WriteMp3Async(OutputPath(), cancellation.Token));
    }

    [Fact]
    public async Task Chunks_with_different_sample_rates_are_rejected()
    {
        await using var workspace = new Mp3AudioWorkspace();
        await workspace.AddChunkAsync(Tone(440, 0.5, 24000), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.AddChunkAsync(Tone(440, 0.5, 16000), CancellationToken.None));
    }

    [Fact]
    public async Task Only_mono_audio_and_whole_samples_are_accepted()
    {
        await using var workspace = new Mp3AudioWorkspace();

        await Assert.ThrowsAsync<NotSupportedException>(() => workspace.AddChunkAsync(new SpeechAudio(new byte[8], Rate, 2), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.AddChunkAsync(new SpeechAudio(new byte[7], Rate, 1), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.AddChunkAsync(new SpeechAudio(Array.Empty<byte>(), Rate, 1), CancellationToken.None));
    }

    [Fact]
    public void The_factory_creates_a_new_workspace_each_time()
    {
        var factory = new Mp3AudioWorkspaceFactory();

        var first = factory.Create();
        var second = factory.Create();

        Assert.NotSame(first, second);
    }
}
