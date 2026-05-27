using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>PRD-023f: remove visual catalog entries and blobs for a forgotten person.</summary>
public interface IVisualPersonPrivacyPurge
{
    Task<VisualPersonPurgeResult> PurgePersonAsync(
        string projectRoot,
        string scenarioId,
        string entityKey,
        CancellationToken cancellationToken = default);
}

public sealed class VisualPersonPurgeResult
{
    public int AssetsRemoved { get; init; }

    public int BlobsDeleted { get; init; }
}
