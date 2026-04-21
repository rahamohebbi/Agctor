using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;
using AgctorSDK.Core.ProjectMemory.Resolution.Signals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Resolution;

[TestClass]
public sealed class EmbeddingSimilarityTests
{
    private sealed class FakeProvider : IEmbeddingProvider
    {
        public bool IsAvailable => true;
        public float[]? Vector { get; set; }
        public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(Vector);
    }

    [TestMethod]
    public void NullProvider_Returns_Null_Signal()
    {
        var s = new EmbeddingSimilarity(new NullEmbeddingProvider()).Score(new SignalContext
        {
            SessionAssertedFacts = new[] { "fact" },
            CandidateEntityPath = "/tmp"
        }, new ResolutionPolicy());
        Assert.IsNull(s);
    }

    [TestMethod]
    public void Provider_Returning_Null_Yields_Null()
    {
        var p = new FakeProvider { Vector = null };
        var s = new EmbeddingSimilarity(p).Score(new SignalContext
        {
            SessionAssertedFacts = new[] { "fact" },
            CandidateEntityPath = "/tmp"
        }, new ResolutionPolicy());
        Assert.IsNull(s);
    }
}
