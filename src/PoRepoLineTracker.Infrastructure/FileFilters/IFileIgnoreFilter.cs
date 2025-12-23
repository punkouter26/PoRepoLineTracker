namespace PoRepoLineTracker.Infrastructure.FileFilters;

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
}
