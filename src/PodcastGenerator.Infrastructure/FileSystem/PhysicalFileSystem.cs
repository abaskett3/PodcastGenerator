using System.Text;
using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Infrastructure.FileSystem;

/// <summary><see cref="IFileSystem"/> over <c>System.IO</c>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    // File.ReadAllTextAsync detects a UTF-8 byte order mark and otherwise reads UTF-8 (D-16).
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);

    public async Task<bool> TryWriteNewTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream, Utf8NoBom);
            await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    public async Task<bool> TryWriteNewPrivateTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        try
        {
            await WritePrivateAsync(path, contents, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    public async Task ReplacePrivateTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        // Write a new file next to the target, then move it over the target, so a crash or a full disk while writing never
        // leaves a half-written key file. The new file is created owner-only, so the result is too.
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WritePrivateAsync(temporaryPath, contents, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public void MoveFile(string source, string destination) => File.Move(source, destination, overwrite: false);

    public void DeleteFile(string path) => File.Delete(path);

    /// <summary>Creates a new file and writes the text. On Unix the file is created with mode 0600 (owner read and write),
    /// which is applied at creation, so the contents are never readable by others. <c>FileStreamOptions.UnixCreateMode</c>
    /// throws <see cref="PlatformNotSupportedException"/> on Windows (checked on Windows 11 with .NET 10), so it is set only
    /// elsewhere; on Windows the file inherits the permissions of its folder.</summary>
    private static async Task WritePrivateAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(path, options);
        await using var writer = new StreamWriter(stream, Utf8NoBom);
        await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: the original failure is the one to report.
        }
    }
}
