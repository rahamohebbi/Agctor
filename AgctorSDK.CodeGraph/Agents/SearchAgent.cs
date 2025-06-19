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

        public SearchAgent(string id,
                           IEmbeddingGenerator generator,
                           EmbeddingStoreActor storeActor,
                           CodeGraphActorBase root)
            : base(id)
        {
            _generator = generator;
            _storeActor = storeActor;
            _root = root;
        }

        /// <summary>
        /// Parameter-less constructor for DI / reflection. Be sure to call <see cref="Configure"/> before use.
        /// </summary>
        public SearchAgent() { }

        public void Configure(IEmbeddingGenerator generator,
                              EmbeddingStoreActor storeActor,
                              CodeGraphActorBase root)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _storeActor = storeActor ?? throw new ArgumentNullException(nameof(storeActor));
            _root = root ?? throw new ArgumentNullException(nameof(root));
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
            if (IsListClassesCommand(prompt))
            {
                var classes = EnumerateClasses(_root).Distinct().ToList();

                // Fallback: if no ClassActor nodes yet, analyze files on the fly
                if (classes.Count == 0)
                {
                    foreach (var file in EnumerateFiles(_root))
                    {
                        var parsed = await ((FileActor)file).AnalyzeAsync(new AnalyzerRegistry { }, null);
                        classes.AddRange(parsed.Classes.Select(c => c.Name));
                    }
                    classes = classes.Distinct().OrderBy(c => c).ToList();
                }

                return classes.Count == 0 ? "No classes found in source." : string.Join("\n", classes);
            }

            // Line-count query e.g., "how many lines of code is Calculator class"
            if (TryExtractLineCountRequest(prompt, out var className))
            {
                var file = FindFileContainingClass(_root, className);
                if (file == null)
                {
                    return $"Class '{className}' not found.";
                }

                var lines = System.IO.File.ReadAllLines(file.PhysicalPath!);
                int start = Array.FindIndex(lines, l => l.Contains($"class {className}"));
                if (start == -1)
                {
                    return $"Unable to locate class declaration for '{className}'.";
                }
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
                int count = end - start + 1;
                return $"Class {className} has approximately {count} lines of code (including whitespace).";
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
                    sb.AppendLine($"{node.GetType().Name}: {node.Name} (score {score:F2})");
                }
            }
            return sb.ToString();
        }

        protected override async Task ProcessPromptInternalAsync(string prompt, CancellationToken cancellationToken)
        {
            // For this agent we do synchronous answer via ReceiveAsync override; we don't need long-running task flow.
            // However, base class still expects something; mark completed immediately.
            await FinalizeTask("Search completed", cancellationToken);
        }

        private static bool IsListClassesCommand(string prompt)
            => prompt.Trim().ToLowerInvariant().StartsWith("list") && prompt.Contains("class", StringComparison.OrdinalIgnoreCase);

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

        private static bool TryExtractLineCountRequest(string prompt, out string className)
        {
            className = string.Empty;
            var lowered = prompt.ToLowerInvariant();
            if (!lowered.Contains("lines of code")) return false;

            // naive extraction: assume pattern "<class> class" earlier words
            var tokens = prompt.Split(new[] { ' ', '?', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                if (tokens[i + 1].Equals("class", StringComparison.OrdinalIgnoreCase))
                {
                    className = tokens[i];
                    return true;
                }
            }
            return false;
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
    }
} 