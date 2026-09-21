using System.Text;
using PodcastGenerator.Infrastructure.FileSystem;
using PodcastGenerator.Infrastructure.Speech;

namespace PodcastGenerator.UnitTests.Infrastructure;

/// <summary>The real file system adapter, tested in a temporary directory that each test deletes (AC-5).</summary>
public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pg-fs-" + Guid.NewGuid().ToString("N"));
    private readonly PhysicalFileSystem _fileSystem = new();

    public PhysicalFileSystemTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string PathOf(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task A_new_file_is_written_as_utf8_without_a_byte_order_mark()
    {
        var written = await _fileSystem.TryWriteNewTextAsync(PathOf("style.md"), "café {transcript}", CancellationToken.None);

        Assert.True(written);
        var bytes = await File.ReadAllBytesAsync(PathOf("style.md"));
        Assert.Equal(Encoding.UTF8.GetBytes("café {transcript}"), bytes);
    }

    [Fact]
    public async Task An_existing_file_is_left_untouched_by_a_new_write()
    {
        await File.WriteAllTextAsync(PathOf("style.md"), "MY EDIT");

        var written = await _fileSystem.TryWriteNewTextAsync(PathOf("style.md"), "DEFAULT", CancellationToken.None);

        Assert.False(written);
        Assert.Equal("MY EDIT", await File.ReadAllTextAsync(PathOf("style.md")));
    }

    [Fact]
    public async Task Reading_accepts_a_byte_order_mark_and_non_ascii_text()
    {
        await File.WriteAllBytesAsync(PathOf("script.txt"), [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("one — two")]);

        var text = await _fileSystem.ReadAllTextAsync(PathOf("script.txt"), CancellationToken.None);

        Assert.Equal("one — two", text);
    }

    [Fact]
    public async Task A_move_never_overwrites_an_existing_file()
    {
        await File.WriteAllTextAsync(PathOf("source.tmp"), "NEW");
        await File.WriteAllTextAsync(PathOf("target.mp3"), "PRECIOUS");

        Assert.Throws<IOException>(() => _fileSystem.MoveFile(PathOf("source.tmp"), PathOf("target.mp3")));

        Assert.Equal("PRECIOUS", await File.ReadAllTextAsync(PathOf("target.mp3")));
        Assert.True(File.Exists(PathOf("source.tmp")));
    }

    [Fact]
    public async Task A_move_to_a_free_name_moves_the_file()
    {
        await File.WriteAllTextAsync(PathOf("source.tmp"), "NEW");

        _fileSystem.MoveFile(PathOf("source.tmp"), PathOf("target.mp3"));

        Assert.Equal("NEW", await File.ReadAllTextAsync(PathOf("target.mp3")));
        Assert.False(File.Exists(PathOf("source.tmp")));
    }

    [Fact]
    public void Deleting_a_missing_file_does_nothing_and_creating_directories_is_recursive()
    {
        _fileSystem.DeleteFile(PathOf("missing.tmp"));
        var nested = Path.Combine(_directory, "a", "b", "c");

        _fileSystem.CreateDirectory(nested);

        Assert.True(_fileSystem.DirectoryExists(nested));
        Assert.False(_fileSystem.FileExists(nested));
    }

    // cli-set-config: the key file is written privately and replaced as one step.
    [Fact]
    public async Task A_new_private_file_is_written_as_utf8_without_a_byte_order_mark_and_is_owner_only_on_Unix()
    {
        var written = await _fileSystem.TryWriteNewPrivateTextAsync(PathOf("PodcastGenerator.env"), "KEY=café\n", CancellationToken.None);

        Assert.True(written);
        Assert.Equal(Encoding.UTF8.GetBytes("KEY=café\n"), await File.ReadAllBytesAsync(PathOf("PodcastGenerator.env")));
        AssertOwnerOnlyOnUnix(PathOf("PodcastGenerator.env"));
    }

    [Fact]
    public async Task An_existing_file_is_left_untouched_by_a_new_private_write()
    {
        await File.WriteAllTextAsync(PathOf("PodcastGenerator.env"), "KEY=mine\n");

        var written = await _fileSystem.TryWriteNewPrivateTextAsync(PathOf("PodcastGenerator.env"), string.Empty, CancellationToken.None);

        Assert.False(written);
        Assert.Equal("KEY=mine\n", await File.ReadAllTextAsync(PathOf("PodcastGenerator.env")));
    }

    [Fact]
    public async Task Replacing_creates_a_missing_file_and_replaces_an_existing_one_leaving_no_temporary_file()
    {
        await _fileSystem.ReplacePrivateTextAsync(PathOf("PodcastGenerator.env"), "A=1\n", CancellationToken.None);
        Assert.Equal("A=1\n", await File.ReadAllTextAsync(PathOf("PodcastGenerator.env")));

        await _fileSystem.ReplacePrivateTextAsync(PathOf("PodcastGenerator.env"), "A=1\nB=2\n", CancellationToken.None);

        Assert.Equal("A=1\nB=2\n", await File.ReadAllTextAsync(PathOf("PodcastGenerator.env")));
        Assert.Equal(["PodcastGenerator.env"], Directory.GetFileSystemEntries(_directory).Select(Path.GetFileName));
        AssertOwnerOnlyOnUnix(PathOf("PodcastGenerator.env"));
    }

    [Fact]
    public async Task A_replaced_file_has_no_byte_order_mark()
    {
        await File.WriteAllBytesAsync(PathOf("PodcastGenerator.env"), [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("A=1\n")]);

        await _fileSystem.ReplacePrivateTextAsync(PathOf("PodcastGenerator.env"), "A=1\n", CancellationToken.None);

        Assert.Equal(Encoding.UTF8.GetBytes("A=1\n"), await File.ReadAllBytesAsync(PathOf("PodcastGenerator.env")));
    }

    [Fact]
    public async Task A_failed_replace_leaves_the_target_as_it_was_and_removes_the_temporary_file()
    {
        // A directory at the target path makes the final move fail after the new file has been written.
        Directory.CreateDirectory(PathOf("PodcastGenerator.env"));

        await Assert.ThrowsAnyAsync<Exception>(() => _fileSystem.ReplacePrivateTextAsync(PathOf("PodcastGenerator.env"), "A=1\n", CancellationToken.None));

        Assert.True(Directory.Exists(PathOf("PodcastGenerator.env")));
        Assert.Equal(["PodcastGenerator.env"], Directory.GetFileSystemEntries(_directory).Select(Path.GetFileName));
    }

    /// <summary>Unix only: <c>FileStreamOptions.UnixCreateMode</c> is not available on Windows, where the file inherits its
    /// folder's permissions. The Linux job of the pull request workflow runs this check.</summary>
    private static void AssertOwnerOnlyOnUnix(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public void Environment_variables_are_read_from_the_process()
    {
        var name = "PG_TEST_" + Guid.NewGuid().ToString("N");
        var variables = new SystemEnvironmentVariables();
        Assert.Null(variables.Get(name));

        Environment.SetEnvironmentVariable(name, "value");
        try
        {
            Assert.Equal("value", variables.Get(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task The_delayer_waits_and_honors_cancellation()
    {
        var delayer = new TaskDelayer();
        await delayer.DelayAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayer.DelayAsync(TimeSpan.FromMinutes(5), cancellation.Token));
    }
}
