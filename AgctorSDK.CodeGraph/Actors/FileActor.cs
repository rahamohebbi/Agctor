using System;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;

namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Represents a source code file within a project. Contains <see cref="ClassActor"/> children.
    /// </summary>
    public sealed class FileActor : CodeGraphActorBase
    {
        private ParsedFile? _cachedParsed;

        public FileActor(string name, string filePath) : base(name, filePath)
        {
        }

        public void AddClass(ClassActor @class) => AddChild(@class);

        public async Task<ParsedFile> AnalyzeAsync(AnalyzerRegistry registry, string? sourceOverride = null)
        {
            if (_cachedParsed != null) return _cachedParsed;

            var extension = System.IO.Path.GetExtension(PhysicalPath ?? "").ToLowerInvariant();
            var analyzer = registry.GetAnalyzerForExtension(extension);
            if (analyzer == null)
            {
                throw new InvalidOperationException($"No analyzer registered for files with extension '{extension}'.");
            }

            var source = sourceOverride ?? (PhysicalPath != null && System.IO.File.Exists(PhysicalPath) ? await System.IO.File.ReadAllTextAsync(PhysicalPath) : string.Empty);
            _cachedParsed = await analyzer.AnalyzeAsync(PhysicalPath ?? string.Empty, source);
            return _cachedParsed;
        }
    }
} 