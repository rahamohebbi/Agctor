using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Abstractions
{
    public class DefaultFileSystem : IFileSystem
    {
        public Task<string> ReadAllTextAsync(string path)
        {
            return File.ReadAllTextAsync(path);
        }

        public Task WriteAllTextAsync(string path, string contents)
        {
            return File.WriteAllTextAsync(path, contents);
        }

        public Task<string[]> ReadAllLinesAsync(string path)
        {
            return File.ReadAllLinesAsync(path);
        }

        public Task WriteAllLinesAsync(string path, IEnumerable<string> contents)
        {
            return File.WriteAllLinesAsync(path, contents);
        }
    }
} 