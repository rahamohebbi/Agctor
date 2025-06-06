using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Abstractions
{
    public interface IFileSystem
    {
        Task<string> ReadAllTextAsync(string path);
        Task WriteAllTextAsync(string path, string contents);
        Task<string[]> ReadAllLinesAsync(string path);
        Task WriteAllLinesAsync(string path, IEnumerable<string> contents);
    }
} 