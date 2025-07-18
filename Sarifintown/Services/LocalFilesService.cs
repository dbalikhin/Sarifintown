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

    public class DirectoryPicker
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public List<string> Subdirectories { get; set; } = new List<string>();

        public override bool Equals(object obj)
        {
            if (obj is DirectoryPicker other)
            {
                return Id == other.Id && Name == other.Name;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name);
        }
    }
}
