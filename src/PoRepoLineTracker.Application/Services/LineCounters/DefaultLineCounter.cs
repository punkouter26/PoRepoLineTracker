using PoRepoLineTracker.Application.Interfaces;
using System.IO;
using System.Threading.Tasks;

namespace PoRepoLineTracker.Application.Services.LineCounters
{
    public class DefaultLineCounter : ILineCounter
    {
        public string FileExtension => "*"; // Represents any file extension not specifically handled

        public async Task<int> CountLinesAsync(Stream stream)
        {
            int lines = 0;
            using (var reader = new StreamReader(stream))
            {
                while (await reader.ReadLineAsync() != null)
                {
                    lines++;
                }
            }
            return lines;
        }
    }
}
