#nullable disable
namespace Sarifintown.Models
{
    public class SarifFile
    {
        public string Filename { get; }
        public long Size { get; }
        public SarifLog SarifLog { get; }      

        public SarifFile(string filename, long size, SarifLog sarifLog)
        {
            Filename = filename;
            Size = size;
            SarifLog = sarifLog;
        }

        public override bool Equals(object obj)
        {
            if (obj is SarifFile other)
            {
                return Filename == other.Filename && Size == other.Size;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine(Filename, Size);
        }
    }
}
