namespace PoRepoLineTracker.API.Storage;

/// <summary>
/// Provides file and directory filtering functionality for line counting operations.
/// Determines which files and directories should be excluded from source code analysis.
/// </summary>
public interface IFileIgnoreFilter
{
    /// <summary>
    /// Determines whether a file should be ignored during line counting.
    /// </summary>
    /// <param name="fileName">The name of the file (e.g., "Program.cs").</param>
    /// <param name="filePath">The full path to the file within the repository.</param>
    /// <returns><c>true</c> if the file should be ignored; otherwise, <c>false</c>.</returns>
    bool ShouldIgnoreFile(string fileName, string filePath);

    /// <summary>
    /// Determines whether a directory should be ignored during repository traversal.
    /// </summary>
    /// <param name="directoryPath">The path to the directory.</param>
    /// <returns><c>true</c> if the directory should be ignored; otherwise, <c>false</c>.</returns>
    bool ShouldIgnoreDirectory(string directoryPath);

    /// <summary>
    /// Determines whether a directory should be ignored, given the names of its immediate
    /// children.
    /// </summary>
    /// <remarks>
    /// The path-only overload can only recognise vendored code whose *name* gives it away
    /// (<c>node_modules</c>, <c>vendor</c>, a reverse-domain package folder). It cannot recognise
    /// the common and much larger case: an entire third-party repository copied into a folder the
    /// author named themselves. Those are identified by what they CONTAIN — a licence, a
    /// <c>.github</c> directory, a <c>.gitmodules</c> file — which needs the child names.
    /// </remarks>
    /// <param name="directoryPath">The path to the directory.</param>
    /// <param name="entryNames">Names of the directory's immediate children (files and folders).</param>
    /// <returns><c>true</c> if the directory should be ignored; otherwise, <c>false</c>.</returns>
    bool ShouldIgnoreDirectory(string directoryPath, IEnumerable<string> entryNames);
}
