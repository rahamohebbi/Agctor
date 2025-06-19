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
            if (root is FileActor file)
            {
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
                        var vec = await _generator.GenerateEmbeddingAsync(cls.Name);
                        await _storeActor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage(cls.Name, vec, cls.Name)));
                        foreach (var m in cls.Methods)
                        {
                            var vecM = await _generator.GenerateEmbeddingAsync(m.Name);
                            await _storeActor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage(m.Name, vecM, m.Name)));
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
            await _storeActor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage(clsActor.Name, vec, clsActor.Name)));

            foreach (var method in clsActor.Children.OfType<MethodActor>())
            {
                var vecM = await _generator.GenerateEmbeddingAsync(method.Name);
                await _storeActor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage(method.Name, vecM, method.Name)));
            }
        }

        protected override bool ShouldDecomposeTask(string prompt) => false; // never decompose
    }
} 