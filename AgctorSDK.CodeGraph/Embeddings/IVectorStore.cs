using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgctorSDK.CodeGraph.Embeddings
{
    public record VectorRecord(string ActorId, float[] Vector, string Text);

    public interface IVectorStore
    {
        Task UpsertAsync(VectorRecord record);
        Task<IEnumerable<(string ActorId, float Score)>> QueryAsync(float[] vector, int k = 5);
        Task<int> CountAsync();
    }
} 