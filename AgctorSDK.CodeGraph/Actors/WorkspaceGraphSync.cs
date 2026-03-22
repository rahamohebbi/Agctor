using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Aligns <see cref="ProjectActor"/> file children with files on disk so the dashboard Actor tree
    /// reflects new writes (e.g. CoderAgent WriteFile) without re-running scenario setup.
    /// Idempotent: existing <see cref="FileActor"/> paths are not duplicated.
    /// </summary>
    public static class WorkspaceGraphSync
    {
        // Demo workspaces are flat; keep top-level only to avoid scanning bin/obj trees.
        private static readonly string[] DiscoverPatterns =
        {
            "*.cs", "*.csproj", "*.sln", "*.md", "*.json", "*.txt", "*.ts", "*.tsx", "*.js", "*.jsx", "*.py", "*.java", "*.xml"
        };

        /// <summary>
        /// For each project under <paramref name="solution"/>, attaches <see cref="FileActor"/> nodes for new disk files.
        /// </summary>
        public static void SyncSolutionFromDisk(SolutionActor solution)
        {
            if (solution == null)
            {
                throw new ArgumentNullException(nameof(solution));
            }

            foreach (var project in solution.Children.OfType<ProjectActor>())
            {
                SyncProjectFromDisk(project);
            }
        }

        /// <summary>
        /// Adds <see cref="FileActor"/> children for files in the project directory that are not already in the graph.
        /// </summary>
        public static void SyncProjectFromDisk(ProjectActor project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            var projPath = project.PhysicalPath;
            if (string.IsNullOrWhiteSpace(projPath))
            {
                return;
            }

            var dir = Path.GetDirectoryName(projPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return;
            }

            var existing = new HashSet<string>(
                project.Children.OfType<FileActor>()
                    .Select(f => NormalizePath(f.PhysicalPath))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(p => p!),
                StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in DiscoverPatterns)
            {
                foreach (var fullPath in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                {
                    var norm = NormalizePath(fullPath);
                    if (string.IsNullOrEmpty(norm) || existing.Contains(norm))
                    {
                        continue;
                    }

                    var name = Path.GetFileName(fullPath);
                    if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    project.AddFile(new FileActor(name, fullPath));
                    existing.Add(norm);
                }
            }
        }

        private static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path.Trim();
            }
        }
    }
}
