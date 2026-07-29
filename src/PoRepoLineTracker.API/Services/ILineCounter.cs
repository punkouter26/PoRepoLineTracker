namespace PoRepoLineTracker.API.Services
{
    // GoF Strategy Pattern: Abstract strategy for line-counting algorithms.
    // Each file extension can have its own counting logic (e.g., ignore comments in .cs files).
    // DefaultLineCounter serves as the default concrete strategy for extensions without a specialized counter.
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
}
