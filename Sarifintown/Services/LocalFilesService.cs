using Sarifintown.Models;

namespace Sarifintown.Services
{
    public class LocalFilesService
    {
        public  List<DirectoryPicker> _openDirectories = new();

        public LocalFilesService()
        {            
        }

        public void AddDirectory(DirectoryPicker directory)
        {
            if (!_openDirectories.Contains(directory))
            {
                _openDirectories.Add(directory);
            }
        }

        public IEnumerable<DirectoryPicker> AllDirectories
        {
            get { return _openDirectories; }
        }

    }
}
