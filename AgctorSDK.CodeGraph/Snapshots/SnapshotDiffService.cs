using System.Collections.Generic;
using System.Linq;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;

namespace AgctorSDK.CodeGraph.Snapshots
{
    public class SnapshotDiffResult
    {
        public List<string> AddedClasses { get; } = new();
        public List<string> RemovedClasses { get; } = new();
        public List<string> AddedMethods { get; } = new();
        public List<string> RemovedMethods { get; } = new();
    }

    public static class SnapshotDiffService
    {
        public static SnapshotDiffResult Diff(CodeGraphActorBase oldSnapshot, CodeGraphActorBase newSnapshot, Analyzers.AnalyzerRegistry registry)
        {
            var oldMap = BuildClassMethodSet(oldSnapshot, registry);
            var newMap = BuildClassMethodSet(newSnapshot, registry);

            var diff = new SnapshotDiffResult();
            foreach (var cls in newMap.Keys.Except(oldMap.Keys)) diff.AddedClasses.Add(cls);
            foreach (var cls in oldMap.Keys.Except(newMap.Keys)) diff.RemovedClasses.Add(cls);

            foreach (var cls in newMap.Keys.Intersect(oldMap.Keys))
            {
                var oldMethods = oldMap[cls];
                var newMethods = newMap[cls];
                diff.AddedMethods.AddRange(newMethods.Except(oldMethods).Select(m => $"{cls}.{m}"));
                diff.RemovedMethods.AddRange(oldMethods.Except(newMethods).Select(m => $"{cls}.{m}"));
            }
            return diff;
        }

        private static Dictionary<string, HashSet<string>> BuildClassMethodSet(CodeGraphActorBase root, Analyzers.AnalyzerRegistry registry)
        {
            var dict = new Dictionary<string, HashSet<string>>();
            foreach (var child in root.Children)
            {
                switch (child)
                {
                    case ClassActor clsActor:
                        if (!dict.TryGetValue(clsActor.Name, out var set))
                        {
                            set = new HashSet<string>();
                            dict[clsActor.Name] = set;
                        }
                        foreach (var m in clsActor.Children.OfType<MethodActor>())
                            set.Add(m.Name);
                        break;
                    case FileActor file:
                        if (file.Children.OfType<ClassActor>().Any())
                        {
                            foreach (var clsActor2 in file.Children.OfType<ClassActor>())
                            {
                                if (!dict.TryGetValue(clsActor2.Name, out var set2))
                                {
                                    set2 = new HashSet<string>();
                                    dict[clsActor2.Name] = set2;
                                }
                                foreach (var m2 in clsActor2.Children.OfType<MethodActor>())
                                    set2.Add(m2.Name);
                            }
                        }
                        else
                        {
                            var parsed = file.AnalyzeAsync(registry, null).Result;
                            foreach (var cls in parsed.Classes)
                            {
                                if (!dict.TryGetValue(cls.Name, out var s))
                                {
                                    s = new HashSet<string>();
                                    dict[cls.Name] = s;
                                }
                                foreach (var m in cls.Methods)
                                    s.Add(m.Name);
                            }
                        }
                        break;
                    default:
                        {
                            var partial = BuildClassMethodSet(child, registry);
                            foreach (var kv in partial)
                            {
                                if (!dict.TryGetValue(kv.Key, out var existing))
                                {
                                    dict[kv.Key] = new HashSet<string>(kv.Value);
                                }
                                else
                                {
                                    existing.UnionWith(kv.Value);
                                }
                            }
                        }
                        break;
                }
            }
            return dict;
        }
    }
} 