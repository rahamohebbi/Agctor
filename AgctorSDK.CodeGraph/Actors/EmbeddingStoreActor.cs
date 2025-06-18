using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Embeddings;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Actor wrapper around an <see cref="IVectorStore"/>.
    /// </summary>
    public sealed class EmbeddingStoreActor : CodeGraphActorBase
    {
        private readonly IVectorStore _store;

        public EmbeddingStoreActor(string id, IVectorStore store) : base("EmbeddingStore", null, id)
        {
            _store = store;
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            switch (envelope.Payload)
            {
                case UpsertEmbeddingMessage up:
                    await _store.UpsertAsync(new VectorRecord(up.ActorId, up.Vector, up.Text));
                    return envelope;
                case QueryEmbeddingMessage qry:
                    var matches = await _store.QueryAsync(qry.Vector, qry.K);
                    return envelope.WithPayload(new QueryResultMessage(matches));
                default:
                    return await base.ReceiveAsync(envelope, cancellationToken);
            }
        }
    }

    public record UpsertEmbeddingMessage(string ActorId, float[] Vector, string Text);
    public record QueryEmbeddingMessage(float[] Vector, int K = 5);
    public record QueryResultMessage(System.Collections.Generic.IEnumerable<(string ActorId, float Score)> Results);
} 