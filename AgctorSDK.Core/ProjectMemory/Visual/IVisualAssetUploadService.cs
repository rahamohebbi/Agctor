using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;
using AgctorSDK.Core.ProjectMemory.Visual.Models;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Host-facing facade for visual upload init/complete (actor-backed).</summary>
public interface IVisualAssetUploadService
{
    Task<VisualAssetInitUploadResult> InitUploadAsync(
        VisualAssetInitUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<VisualAssetCompleteUploadResult> CompleteUploadAsync(
        VisualAssetCompleteUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<VisualAssetRecord?> GetAssetAsync(
        string projectRoot,
        string scenarioId,
        string assetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VisualAssetRecord>> ListAssetsAsync(
        string projectRoot,
        string scenarioId,
        CancellationToken cancellationToken = default);
}
