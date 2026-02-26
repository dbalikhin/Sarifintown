using System.Threading.Tasks;

namespace Sarifintown.Core
{
    public interface IFileReader
    {
        //Task<string> ReadFileContentAsync(int directoryId, string fileName);

        Task<string> ReadFileAsync(string relativePath);
    }
}