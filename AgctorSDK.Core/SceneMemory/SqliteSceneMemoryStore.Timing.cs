using System.Threading;
using System.Threading.Tasks;
namespace AgctorSDK.Core.SceneMemory;

public sealed partial class SqliteSceneMemoryStore
{
    public Task<SceneWorkspace> SaveTimedEventAsync(string workspaceId, string shotId, string expectedRevision, SceneTimedEvent item, CancellationToken cancellationToken = default) =>
        ChangeAsync(workspaceId, workspace => SceneTimedEvents.Save(workspace, shotId, expectedRevision, item), cancellationToken);
    public Task<SceneWorkspace> DeleteTimedEventAsync(string workspaceId, string shotId, string eventId, string expectedRevision, CancellationToken cancellationToken = default) =>
        ChangeAsync(workspaceId, workspace => SceneTimedEvents.Delete(workspace, shotId, eventId, expectedRevision), cancellationToken);
}
