using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers;
using AgctorSDK.CodeGraph.Embeddings;
using AgctorSDK.CodeGraph.Extensions;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.CodeGraph.Actors
{
    public sealed class ComprehensionActor : CodeGraphActorBase
    {
        private readonly CodeGraphActorBase _root;
        private readonly AnalyzerRegistry _registry;
        private readonly EmbeddingStoreActor _embeddingStore;

        public ComprehensionActor(CodeGraphActorBase root, AnalyzerRegistry registry, EmbeddingStoreActor store)
            : base("Comprehension", null)
        {
            _root = root;
            _registry = registry;
            _embeddingStore = store;
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            switch (envelope.Payload)
            {
                case FindPublicMethodsMessage find:
                    var methods = new List<MethodDescriptor>();
                    foreach (var (file, parsed) in _root.EnumerateParsedFiles(_registry))
                    {
                        foreach (var cls in parsed.Classes)
                        {
                            if (find.ClassFilter != null && cls.Name != find.ClassFilter) continue;
                            foreach (var m in cls.Methods)
                            {
                                methods.Add(new MethodDescriptor(cls.Name, m.Name, file.PhysicalPath ?? file.Name));
                            }
                        }
                    }
                    return envelope.WithPayload(new PublicMethodsResult(methods));

                case SemanticSearchMessage ss:
                    var resp = await _embeddingStore.ReceiveAsync(new MessageEnvelope(new QueryEmbeddingMessage(await GetQueryVectorAsync(ss.Query), ss.K)));
                    return envelope.WithPayload(((QueryResultMessage)resp.Payload).Results);
                default:
                    return await base.ReceiveAsync(envelope, cancellationToken);
            }
        }

        private async Task<float[]> GetQueryVectorAsync(string query)
        {
            // naive: reuse first registered embedding generator via reflection
            var gen = new Embeddings.InMemoryVectorStore(); // placeholder vector
            return await Task.FromResult(new float[] { 1, 0 });
        }
    }
} 