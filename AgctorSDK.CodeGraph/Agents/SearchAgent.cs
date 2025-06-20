using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Embeddings;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.CodeGraph.Intents;

namespace AgctorSDK.CodeGraph.Agents
{
    /// <summary>
    /// Performs semantic (vector-based) and structural searches over the CodeGraph.
    /// Input: natural-language prompt.
    /// Output: a human-readable answer – either a list of CodeGraph nodes or a free-form summary.
    /// </summary>
    public sealed class SearchAgent : Agent
    {
        private IEmbeddingGenerator _generator = null!;
        private EmbeddingStoreActor _storeActor = null!;
        private CodeGraphActorBase _root = null!;
        private IReadOnlyList<IIntentResolver> _resolvers = Array.Empty<IIntentResolver>();

        public SearchAgent(string id,
                           IEmbeddingGenerator generator,
                           EmbeddingStoreActor storeActor,
                           CodeGraphActorBase root,
                           IEnumerable<IIntentResolver> resolvers)
            : base(id)
        {
            _generator = generator;
            _storeActor = storeActor;
            _root = root;
            _resolvers = resolvers?.ToList() ?? throw new ArgumentNullException(nameof(resolvers));
        }

        /// <summary>
        /// Parameter-less constructor for DI / reflection. Be sure to call <see cref="Configure"/> before use.
        /// </summary>
        public SearchAgent() { }

        public void Configure(IEmbeddingGenerator generator,
                              EmbeddingStoreActor storeActor,
                              CodeGraphActorBase root,
                              IEnumerable<IIntentResolver> resolvers)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _storeActor = storeActor ?? throw new ArgumentNullException(nameof(storeActor));
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _resolvers = resolvers?.ToList() ?? throw new ArgumentNullException(nameof(resolvers));
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            // If this is a prompt message, run search synchronously and return results as payload.
            if (envelope.Headers.TryGetValue("MessageType", out var mt) && mt == "Prompt" && envelope.Payload is string prompt)
            {
                string resultText;
                try
                {
                    resultText = await ExecuteSearchAsync(prompt, cancellationToken);
                }
                catch (Exception ex)
                {
                    resultText = $"Error: {ex.Message}";
                }

                var respHeaders = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["SenderId"] = Id,
                    ["ReceiverId"] = envelope.Headers.GetValueOrDefault("SenderId", "unknown"),
                    ["MessageType"] = "SearchResult"
                };

                return new MessageEnvelope(resultText, null, Guid.NewGuid().ToString(), respHeaders);
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        private async Task<string> ExecuteSearchAsync(string prompt, CancellationToken cancellationToken)
        {
            var intent = ResolveIntent(prompt);
            if (!intent.IsSuccess)
            {
                // Fall back to semantic vector search when intent resolution fails.
                intent = new Intents.IntentResolution(Intents.IntentKind.SemanticSearch, null);
            }

            switch (intent.Kind)
            {
                case IntentKind.ListClasses:
                {
                    var classes = EnumerateClasses(_root).Distinct().OrderBy(c => c).ToList();
                    return classes.Count == 0 ? "No classes found in source." : string.Join("\n", classes);
                }
                case IntentKind.ListFiles:
                {
                    var files = EnumerateFiles(_root).Select(f => f.Name).Distinct().OrderBy(f => f).ToList();
                    return files.Count == 0 ? "No source files in graph." : string.Join("\n", files);
                }
                case IntentKind.ListMethods:
                {
                    var clsName = intent.Slots != null && intent.Slots.TryGetValue("ClassName", out var tmpCls) ? tmpCls : string.Empty;
                    var clsNode = FindClassByName(_root, clsName);
                    if (clsNode == null) return $"Class '{clsName}' not found.";
                    var methods = clsNode.Children.OfType<MethodActor>().Select(m => m.Name).OrderBy(n => n).ToList();
                    return methods.Count == 0 ? $"Class '{clsName}' has no methods." : string.Join("\n", methods);
                }
                case IntentKind.CountLinesClass:
                case IntentKind.CountLinesFile:
                {
                    bool isClass = intent.Kind == IntentKind.CountLinesClass;
                    var nameKey = isClass ? "ClassName" : "FileName";
                    var targetName = intent.Slots != null && intent.Slots.TryGetValue(nameKey, out var tmpName) ? tmpName : string.Empty;

                    if (isClass)
                    {
                        var cls = FindClassByName(_root, targetName);
                        if (cls == null) return $"Class '{targetName}' not found.";
                        int? loc = cls.LinesOfCode;
                        if (loc == null)
                        {
                            var file = FindFileContainingClass(_root, targetName);
                            loc = file != null ? QuickEstimateClassLines(file.PhysicalPath, targetName) : null;
                        }
                        return loc == null ? $"Unable to determine LOC for class '{targetName}'." : $"Class {targetName} has approximately {loc} lines of code (including whitespace).";
                    }
                    else
                    {
                        var file = FindFileByName(_root, targetName);
                        if (file == null) return $"File '{targetName}' not found.";
                        if (file.PhysicalPath == null || !System.IO.File.Exists(file.PhysicalPath)) return $"Unable to determine LOC for file '{targetName}'.";
                        var count = System.IO.File.ReadLines(file.PhysicalPath).Count();
                        return $"File {targetName} has {count} lines of code (including whitespace).";
                    }
                }
                case IntentKind.GetMethodSource:
                {
                    var methodName = intent.Slots != null && intent.Slots.TryGetValue("MethodName", out var mname) ? mname : string.Empty;
                    var methodNode = FindMethodByName(_root, methodName);
                    if (methodNode == null) return $"Method '{methodName}' not found.";
                    var file = FindFileContainingClass(_root, FindParentClass(_root, methodNode.Id)?.Name ?? string.Empty);
                    if (file?.PhysicalPath == null || !System.IO.File.Exists(file.PhysicalPath))
                        return $"Source for method '{methodName}' not available.";
                    var snippet = ExtractMethodSource(file.PhysicalPath, methodName);
                    return string.IsNullOrWhiteSpace(snippet) ? $"Could not extract source for {methodName}." : $"```csharp\n{snippet}\n```";
                }
                case IntentKind.GetClassSource:
                {
                    var className = intent.Slots != null && intent.Slots.TryGetValue("ClassName", out var cname) ? cname : string.Empty;
                    var file = FindFileContainingClass(_root, className);
                    if (file?.PhysicalPath == null || !System.IO.File.Exists(file.PhysicalPath))
                        return $"Source for class '{className}' not available.";
                    var snippet = ExtractClassSource(file.PhysicalPath, className);
                    return string.IsNullOrWhiteSpace(snippet) ? $"Could not extract source for {className}." : $"```csharp\n{snippet}\n```";
                }
                case IntentKind.SemanticSearch:
                default:
                    break; // fall-through to vector search below
            }

            var vec = await _generator.GenerateEmbeddingAsync(prompt);
            var queryMsg = new QueryEmbeddingMessage(vec, 5);
            var envelope = new MessageEnvelope(queryMsg);
            var resp = await _storeActor.ReceiveAsync(envelope, cancellationToken);

            if (resp.Payload is not QueryResultMessage results)
            {
                throw new InvalidOperationException("Unexpected payload from EmbeddingStoreActor");
            }

            var sb = new StringBuilder();
            foreach (var (actorId, score) in results.Results)
            {
                var node = FindNodeById(_root, actorId);
                if (node != null)
                {
                    sb.AppendLine(BuildNodeDescription(node, score, _root));
                    sb.AppendLine();
                }
            }
            return sb.ToString().Trim();
        }

        protected override async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
        {
            // For this agent we do synchronous answer via ReceiveAsync override; we don't need long-running task flow.
            // However, base class still expects something; mark completed immediately.
            await FinalizeTask("Search completed", cancellationToken);
        }

        private Intents.IntentResolution ResolveIntent(string prompt)
        {
            foreach (var resolver in _resolvers)
            {
                var res = resolver.Resolve(prompt);
                if (res.IsSuccess) return res;
            }
            return Intents.IntentResolution.Unresolved;
        }

        private static IEnumerable<string> EnumerateClasses(CodeGraphActorBase root)
        {
            foreach (var child in root.Children)
            {
                switch (child)
                {
                    case ClassActor cls:
                        yield return cls.Name;
                        foreach (var nested in EnumerateClasses(cls))
                            yield return nested;
                        break;
                    default:
                        foreach (var nested in EnumerateClasses(child))
                            yield return nested;
                        break;
                }
            }
        }

        private static CodeGraphActorBase? FindNodeById(CodeGraphActorBase root, string id)
        {
            if (root.Id == id) return root;
            foreach (var child in root.Children)
            {
                var n = FindNodeById(child, id);
                if (n != null) return n;
            }
            return null;
        }

        protected override bool ShouldDecomposeTask(string prompt) => false; // single-step processing

        private static IEnumerable<FileActor> EnumerateFiles(CodeGraphActorBase root)
        {
            if (root is FileActor f) yield return f;
            foreach (var child in root.Children)
                foreach (var f2 in EnumerateFiles(child))
                    yield return f2;
        }

        private static ClassActor? FindClassByName(CodeGraphActorBase root, string name)
        {
            foreach (var child in root.Children)
            {
                if (child is ClassActor cls && cls.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return cls;
                var nested = FindClassByName(child, name);
                if (nested != null) return nested;
            }
            return null;
        }

        private static FileActor? FindFileByName(CodeGraphActorBase root, string fileName)
        {
            return EnumerateFiles(root).FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static int? QuickEstimateClassLines(string? filePath, string className)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return null;

            var lines = System.IO.File.ReadAllLines(filePath);
            int start = Array.FindIndex(lines, l => l.Contains($"class {className}"));
            if (start == -1) return null;
            int depth = 0;
            int end = start;
            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i];
                depth += CountChar(line, '{') - CountChar(line, '}');
                if (i > start && depth <= 0)
                {
                    end = i;
                    break;
                }
            }
            return end - start + 1;
        }

        private static int CountChar(string s, char c) => s.Count(ch => ch == c);

        private static FileActor? FindFileContainingClass(CodeGraphActorBase root, string className)
        {
            foreach (var f in EnumerateFiles(root))
            {
                var text = System.IO.File.ReadAllText(f.PhysicalPath!);
                if (text.Contains($"class {className}"))
                    return f;
            }
            return null;
        }

        #region Description builder for vector search

        private static string BuildNodeDescription(CodeGraphActorBase node, float score, CodeGraphActorBase root)
        {
            switch (node)
            {
                case ClassActor cls:
                {
                    var file = FindFileContainingClass(root, cls.Name);
                    var methods = cls.Children.OfType<MethodActor>().Select(m => m.Name).ToList();
                    int? loc = cls.LinesOfCode ?? (file != null ? QuickEstimateClassLines(file.PhysicalPath, cls.Name) : null);

                    var sb = new StringBuilder();
                    sb.AppendLine($"Class: {cls.Name}    (score {score:F2})");
                    if (file != null) sb.AppendLine($"File : {file.Name}");
                    if (loc != null) sb.AppendLine($"Lines: {loc}");
                    if (methods.Count > 0)
                    {
                        sb.AppendLine("Methods:");
                        foreach (var m in methods) sb.AppendLine($"  • {m}");
                    }
                    return sb.ToString().TrimEnd();
                }
                case MethodActor mth:
                {
                    var parentClass = FindParentClass(root, mth.Id);
                    var sb = new StringBuilder();
                    sb.AppendLine($"Method: {mth.Name}    (score {score:F2})");
                    if (parentClass != null) sb.AppendLine($"Class : {parentClass.Name}");
                    if (mth.LinesOfCode != null) sb.AppendLine($"Lines : {mth.LinesOfCode}");
                    return sb.ToString().TrimEnd();
                }
                case FileActor file:
                {
                    int lines = file.PhysicalPath != null && System.IO.File.Exists(file.PhysicalPath)
                        ? System.IO.File.ReadLines(file.PhysicalPath).Count()
                        : 0;
                    return $"File: {file.Name} (lines {lines}, score {score:F2})";
                }
                default:
                    return $"{node.GetType().Name}: {node.Name} (score {score:F2})";
            }
        }

        private static ClassActor? FindParentClass(CodeGraphActorBase root, string methodId)
        {
            foreach (var cls in EnumerateClasses(root).Select(name => FindClassByName(root, name)).Where(c => c != null))
            {
                if (cls!.Children.OfType<MethodActor>().Any(m => m.Id == methodId))
                    return cls;
            }
            return null;
        }

        private static MethodActor? FindMethodByName(CodeGraphActorBase root, string name)
        {
            foreach (var cls in EnumerateClasses(root).Select(n => FindClassByName(root, n)).Where(c => c != null))
            {
                var method = cls!.Children.OfType<MethodActor>()
                                     .FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (method != null) return method;
            }
            return null;
        }

        private static string ExtractMethodSource(string filePath, string methodName, int maxLines = 40)
        {
            var lines = System.IO.File.ReadAllLines(filePath);
            int start = Array.FindIndex(lines, l => l.Contains(methodName + "("));
            if (start == -1) return string.Empty;

            var sb = new System.Text.StringBuilder();
            int depth = 0;
            for (int i = start; i < lines.Length && sb.Length < maxLines * 200; i++)
            {
                var line = lines[i];
                sb.AppendLine(line);
                depth += CountChar(line, '{') - CountChar(line, '}');
                if (i > start && depth <= 0) break;
                if (i - start >= maxLines) break;
            }
            return sb.ToString();
        }

        private static string ExtractClassSource(string filePath, string className, int maxLines = 120)
        {
            var lines = System.IO.File.ReadAllLines(filePath);
            int start = Array.FindIndex(lines, l => l.Contains("class " + className));
            if (start == -1) return string.Empty;
            var sb = new System.Text.StringBuilder();
            int depth = 0;
            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i];
                sb.AppendLine(line);
                depth += CountChar(line, '{') - CountChar(line, '}');
                if (i > start && depth <= 0) break;
                if (i - start >= maxLines) break;
            }
            return sb.ToString();
        }

        #endregion
    }
} 