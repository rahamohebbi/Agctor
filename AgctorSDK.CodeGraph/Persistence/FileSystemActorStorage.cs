using System.IO;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;
using System.Linq;

namespace AgctorSDK.CodeGraph.Persistence
{
    /// <summary>
    /// Default implementation that stores JSON files under a .agctorstore folder.
    /// </summary>
    public sealed class FileSystemActorStorage : IActorStorage
    {
        private const string ActorsFolder = "actors";

        public async Task SaveAsync(CodeGraphActorBase actor, string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new System.ArgumentNullException(nameof(rootDirectory));
            var actorsDir = Path.Combine(rootDirectory, ActorsFolder);
            Directory.CreateDirectory(actorsDir);
            await SaveRecursiveAsync(actor, actorsDir);
        }

        private static async Task SaveRecursiveAsync(CodeGraphActorBase actor, string actorsDir)
        {
            // Compute path: actorsDir/<actorId>.json (simple scheme)
            var filePath = Path.Combine(actorsDir, $"{actor.Id}.json");
            var dto = ActorSerializer.ToDto(actor);
            await ActorSerializer.WriteAsync(dto, filePath);

            foreach (var child in actor.Children)
            {
                await SaveRecursiveAsync(child, actorsDir);
            }
        }

        public async Task<TActor> LoadAsync<TActor>(string rootDirectory, bool recursive = true) where TActor : CodeGraphActorBase
        {
            var actorsDir = Path.Combine(rootDirectory, ActorsFolder);
            var solutionFile = Directory.GetFiles(actorsDir, "*.json").First(f =>
            {
                var dtoTmp = ActorSerializer.ReadAsync(f).Result;
                return dtoTmp.ActorType == nameof(SolutionActor);
            });
            var dto = await ActorSerializer.ReadAsync(solutionFile);
            var rootActor = (TActor)ActorSerializer.FromDto(dto, recursive ? null : actorsDir);
            return rootActor;
        }

        public Task DeleteStoreAsync(string rootDirectory)
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
            return Task.CompletedTask;
        }
    }
} 