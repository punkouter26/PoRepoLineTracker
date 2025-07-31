using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.Infrastructure.FileFilters;

public interface IFileIgnoreFilter
{
    bool ShouldIgnoreFile(string fileName, string filePath);
    bool ShouldIgnoreDirectory(string directoryPath);
}
