using System.Text;
using PodcastGenerator.Application.Abstractions;

namespace PodcastGenerator.Infrastructure.FileSystem;

/// <summary><see cref="IFileSystem"/> over <c>System.IO</c>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
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
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    public void MoveFile(string source, string destination) => File.Move(source, destination, overwrite: false);

    public void DeleteFile(string path) => File.Delete(path);
}
