using System.Text;
using AgctorSDK.CodeGraph.Snapshots;

namespace AgctorSDK.CodeGraph.Services
{
    public static class DiffFormatterService
    {
        public static string Format(SnapshotDiffResult diff)
        {
            var sb = new StringBuilder();
            if (diff.AddedClasses.Count > 0)
            {
                sb.AppendLine("Added Classes:");
                foreach (var c in diff.AddedClasses) sb.AppendLine($"  + {c}");
            }
            if (diff.AddedMethods.Count > 0)
            {
                sb.AppendLine("Added Methods:");
                foreach (var m in diff.AddedMethods) sb.AppendLine($"  + {m}");
            }
            if (diff.RemovedClasses.Count > 0)
            {
                sb.AppendLine("Removed Classes:");
                foreach (var c in diff.RemovedClasses) sb.AppendLine($"  - {c}");
            }
            if (diff.RemovedMethods.Count > 0)
            {
                sb.AppendLine("Removed Methods:");
                foreach (var m in diff.RemovedMethods) sb.AppendLine($"  - {m}");
            }
            return sb.ToString();
        }
    }
} 