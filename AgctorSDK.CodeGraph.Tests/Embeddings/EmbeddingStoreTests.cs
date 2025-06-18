using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Embeddings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Embeddings
{
    [TestClass]
    public class EmbeddingStoreTests
    {
        [TestMethod]
        public async Task UpsertAndQuery_ShouldReturnNearest()
        {
            var store = new InMemoryVectorStore();
            var actor = new EmbeddingStoreActor("store", store);
            await actor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage("A", new float[] { 1, 0 }, "A")));
            await actor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(new UpsertEmbeddingMessage("B", new float[] { 0, 1 }, "B")));

            var queryMsg = new QueryEmbeddingMessage(new float[] { 1, 0 });
            var resp = await actor.ReceiveAsync(new AgctorSDK.Core.Messages.MessageEnvelope(queryMsg));
            var results = ((QueryResultMessage)resp.Payload).Results.ToList();
            Assert.AreEqual("A", results[0].ActorId);
        }
    }
} 