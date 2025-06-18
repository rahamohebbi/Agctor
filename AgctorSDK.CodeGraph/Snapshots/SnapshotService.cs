using System.IO;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using AgctorSDK.CodeGraph.Persistence;

namespace AgctorSDK.CodeGraph.Snapshots
{
    public static class SnapshotService
    {
        private const string Folder = ".agctorstore/snapshots";

        public static async Task<string> SaveSnapshotAsync(CodeGraphActorBase root, string repositoryPath, string snapshotId)
        {
            var dir = Path.Combine(repositoryPath, Folder);
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, $"{snapshotId}.json");
            var dto = ActorSerializer.ToDto(root);
            await ActorSerializer.WriteAsync(dto, filePath);
            return filePath;
        }

        public static async Task<CodeGraphActorBase> LoadSnapshotAsync(string filePath)
        {
            var dto = await ActorSerializer.ReadAsync(filePath);
            return ActorSerializer.FromDto(dto);
        }
    }
} 