using PoRepoLineTracker.Application.Interfaces;
using System.IO;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Services.LineCounters
{
    public class CSharpLineCounter : ILineCounter
    {
        public string FileExtension => ".cs";

        public async Task<int> CountLinesAsync(Stream stream)
        {
            int lines = 0;
            using (var reader = new StreamReader(stream))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    // Basic example: ignore empty lines and lines that are only whitespace or comments
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//"))
                    {
                        continue;
                    }
                    lines++;
                }
            }
            return lines;
        }
    }
}
