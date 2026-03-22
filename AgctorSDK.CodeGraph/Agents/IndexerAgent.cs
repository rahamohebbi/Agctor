using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Embeddings;

namespace AgctorSDK.CodeGraph.Agents
{
    /// <summary>
    /// Walks the CodeGraph and populates the vector store with embeddings for classes & methods.
    /// </summary>
    public sealed class IndexerAgent : AgctorSDK.Core.Agents.Agent
    {
        private AnalyzerRegistry _registry = null!;
        private IEmbeddingGenerator _generator = null!;
        private EmbeddingStoreActor _storeActor = null!;
        private CodeGraphActorBase? _root;

        public IndexerAgent(string id,
                             AnalyzerRegistry registry,
                             IEmbeddingGenerator generator,
                             EmbeddingStoreActor storeActor)
            : base(id)
        {
            _registry = registry;
            _generator = generator;
            _storeActor = storeActor;
        }

        /// <summary>
        /// Parameterless constructor for reflection-based activation. Internal services must be set via Init.
        /// </summary>
        public IndexerAgent() : base()
        {
        }

        public void Configure(AnalyzerRegistry registry, IEmbeddingGenerator generator, EmbeddingStoreActor storeActor, CodeGraphActorBase root)
        {
            _registry = registry;
            _generator = generator;
            _storeActor = storeActor;
            _root = root;
        }

        protected override async Task ProcessPromptInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            // Any prompt triggers indexing of the configured root.
            if (_root == null)
            {
                await FinalizeTaskAsFailed(new InvalidOperationException("IndexerAgent not configured with root actor"), cancellationToken);
                return;
            }

            await IndexAsync(_root);
            await FinalizeTask("IndexingComplete", cancellationToken);
        }

        public async Task IndexAsync(CodeGraphActorBase root)
        {
            // Pick up files created after scenario setup (e.g. new .md / .cs from chat) so Actor tree + index match disk.
            if (root is SolutionActor solution)
            {
                WorkspaceGraphSync.SyncSolutionFromDisk(solution);
            }

            if (root is FileActor file)
            {
                var ext = Path.GetExtension(file.PhysicalPath ?? string.Empty).ToLowerInvariant();
                if (_registry.GetAnalyzerForExtension(ext) == null)
                {
                    // File is listed in the graph for the dashboard; no embeddings for this extension.
                    return;
                }

                // Prefer explicit class/method children if they already exist (e.g., constructed via tests).
                if (file.Children.Count > 0)
                {
                    foreach (var classActor in file.Children.OfType<ClassActor>())
                    {
                        await IndexClassAsync(classActor);
                    }
                }
                else
                {
                    // Fall-back to analyzer parsing when the graph was created from raw source files.
                    var parsed = await file.AnalyzeAsync(_registry, null);

                    foreach (var cls in parsed.Classes)
                    {
                        // 1. Ensure ClassActor exists in the tree
                        var classActor = file.Children.OfType<ClassActor>()
                                                .FirstOrDefault(c => c.Name == cls.Name);
                        if (classActor == null)
                        {
                            classActor = new ClassActor(cls.Name)
                            {
                                LinesOfCode = TryEstimateClassLines(file.PhysicalPath, cls.Name)
                            };
                            file.AddClass(classActor);
                        }

                        // 2. Generate embedding for class
                        var vec = await _generator.GenerateEmbeddingAsync(cls.Name);
                        await _storeActor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage(classActor.Id, vec, cls.Name)));

                        // 3. Methods
                        foreach (var m in cls.Methods)
                        {
                            var methodActor = classActor.Children.OfType<MethodActor>()
                                                         .FirstOrDefault(mm => mm.Name == m.Name);
                            if (methodActor == null)
                            {
                                methodActor = new MethodActor(m.Name)
                                {
                                    LinesOfCode = TryEstimateMethodLines(file.PhysicalPath, m.Name)
                                };
                                classActor.AddMethod(methodActor);
                            }

                            var vecM = await _generator.GenerateEmbeddingAsync(m.Name);
                            await _storeActor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage(methodActor.Id, vecM, m.Name)));
                        }
                    }
                }
            }

            if (root is ClassActor clsActor)
            {
                await IndexClassAsync(clsActor);
            }

            foreach (var child in root.Children)
            {
                await IndexAsync(child);
            }
        }

        private async Task IndexClassAsync(ClassActor clsActor)
        {
            var vec = await _generator.GenerateEmbeddingAsync(clsActor.Name);
            await _storeActor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage(clsActor.Id, vec, clsActor.Name)));

            foreach (var method in clsActor.Children.OfType<MethodActor>())
            {
                var vecM = await _generator.GenerateEmbeddingAsync(method.Name);
                await _storeActor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage(method.Id, vecM, method.Name)));
            }
        }

        protected override bool ShouldDecomposeTask(string prompt) => false; // never decompose

        #region Helper – LOC estimation

        private static int? TryEstimateClassLines(string? filePath, string className)
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

        private static int? TryEstimateMethodLines(string? filePath, string methodName)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return null;

            var lines = System.IO.File.ReadAllLines(filePath);
            int start = Array.FindIndex(lines, l => l.Contains($"{methodName}("));
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

        #endregion
    }
} 