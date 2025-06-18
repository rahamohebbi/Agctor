using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AgctorSDK.CodeGraph.Embeddings
{
    /// <summary>
    /// Very naive in-memory vector store using cosine similarity for testing purposes.
    /// Not suitable for large datasets but removes external dependencies for unit tests.
    /// </summary>
    public sealed class InMemoryVectorStore : IVectorStore
    {
        private readonly ConcurrentDictionary<string, VectorRecord> _records = new();

        public Task UpsertAsync(VectorRecord record)
        {
            _records[record.ActorId] = record;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<(string ActorId, float Score)>> QueryAsync(float[] vector, int k = 5)
        {
            var results = _records.Values
                .Select(r => (r.ActorId, Score: Cosine(r.Vector, vector)))
                .OrderByDescending(t => t.Score)
                .Take(k);
            return Task.FromResult(results);
        }

        public Task<int> CountAsync() => Task.FromResult(_records.Count);

        private static float Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
        {
            float dot = 0, magA = 0, magB = 0;
            for (int i = 0; i < a.Count; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            return dot / (float)(System.Math.Sqrt(magA) * System.Math.Sqrt(magB) + 1e-6);
        }
    }
} 