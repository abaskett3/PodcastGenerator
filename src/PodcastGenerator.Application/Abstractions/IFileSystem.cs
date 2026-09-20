namespace PodcastGenerator.Application.Abstractions;

/// <summary>The few file system operations the application logic needs. The Infrastructure implementation uses
/// <c>System.IO</c>; tests use temporary directories.</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    /// <summary>Reads a UTF-8 text file. A byte order mark is accepted.</summary>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    /// <summary>Writes a new file. Returns <see langword="false"/> and changes nothing when the file already exists.</summary>
    Task<bool> TryWriteNewTextAsync(string path, string contents, CancellationToken cancellationToken);

    /// <summary>Moves a file. Throws <see cref="IOException"/> when the destination already exists; never overwrites.</summary>
    void MoveFile(string source, string destination);

    /// <summary>Deletes a file. Does nothing when it does not exist.</summary>
    void DeleteFile(string path);
}
