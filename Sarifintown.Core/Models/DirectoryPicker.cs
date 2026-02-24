using System;
using System.Collections.Generic;

namespace Sarifintown.Models
{
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