using System;
using System.Collections.Generic;
using System.Text;

namespace Sarifintown.Core
{
    public interface ITreeSitterEngine
    {
        Task InitializeAsync();
        Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine);
    }
}
