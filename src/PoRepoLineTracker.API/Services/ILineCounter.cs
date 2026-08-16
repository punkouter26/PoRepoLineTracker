namespace PoRepoLineTracker.API.Services;

// Strategy for line-counting algorithms: each tracked file extension gets its own comment
// syntax, with the "*" instance as the fallback for anything without a dedicated one.
// See SourceLineCounter.DefaultSet for the full catalog.
public interface ILineCounter
{
    /// <summary>
    /// Gets the file extension this line counter is responsible for (e.g., ".cs", ".js").
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Counts the lines of code in a given stream, applying specific rules for the file type.
    /// </summary>
    /// <param name="stream">The stream containing the file content.</param>
    /// <returns>The number of lines of code.</returns>
    Task<int> CountLinesAsync(Stream stream);
}
