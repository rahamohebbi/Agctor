using System.Collections.Generic;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;

namespace AgctorSDK.CodeGraph.Extensions
{
    public static class TraversalExtensions
    {
        public static IEnumerable<(FileActor File, ParsedFile Parsed)> EnumerateParsedFiles(this CodeGraphActorBase root, Analyzers.AnalyzerRegistry registry)
        {
            foreach (var child in root.Children)
            {
                if (child is FileActor file)
                {
                    var parsed = file.AnalyzeAsync(registry, null).Result; // sync for simplicity in traversal
                    yield return (file, parsed);
                }
                else
                {
                    foreach (var tuple in child.EnumerateParsedFiles(registry))
                        yield return tuple;
                }
            }
        }
    }
} 