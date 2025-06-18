using AgctorSDK.Core.Tools.Abstractions;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Implementations
{
    /// <summary>
    /// A simple wrapper around the System.IO file system operations implementing IFileSystem.
    /// </summary>
    public class FileSystemWrapper : IFileSystem
    {
        public Task<bool> FileExistsAsync(string path)
        {
            return Task.FromResult(File.Exists(path));
        }

        public Task<bool> DirectoryExistsAsync(string path)
        {
            return Task.FromResult(Directory.Exists(path));
        }

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

        public Task<bool> DeleteFileAsync(string path)
        {
            if (!File.Exists(path))
                return Task.FromResult(false);

            File.Delete(path);
            return Task.FromResult(true);
        }

        public Task CreateDirectoryAsync(string path)
        {
            Directory.CreateDirectory(path);
            return Task.CompletedTask;
        }
    }
} 