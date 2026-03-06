using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sarifintown.Core
{
    public interface ITreeSitterEngine
    {
        Task InitializeAsync();
        Task<string> ExtractMethodAsync(string sourceCode, string language, int startLine, int endLine, CancellationToken cancellationToken = default);
    }
}
