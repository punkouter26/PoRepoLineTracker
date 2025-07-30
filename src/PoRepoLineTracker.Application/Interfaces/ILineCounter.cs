namespace PoRepoLineTracker.Application.Interfaces
{
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
