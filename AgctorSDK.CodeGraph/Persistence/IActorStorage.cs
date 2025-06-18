using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Actors;

namespace AgctorSDK.CodeGraph.Persistence
{
    /// <summary>
    /// Abstraction for persisting actor hierarchies to and from durable storage (file system, database, etc.).
    /// </summary>
    public interface IActorStorage
    {
        /// <summary>
        /// Saves the specified <paramref name="actor"/> and its children to <paramref name="rootDirectory"/>.
        /// </summary>
        Task SaveAsync(CodeGraphActorBase actor, string rootDirectory);

        /// <summary>
        /// Loads an actor hierarchy previously saved in <paramref name="rootDirectory"/>.
        /// </summary>
        Task<TActor> LoadAsync<TActor>(string rootDirectory, bool recursive = true) where TActor : CodeGraphActorBase;

        /// <summary>
        /// Deletes the entire persisted store at <paramref name="rootDirectory"/>.
        /// </summary>
        Task DeleteStoreAsync(string rootDirectory);
    }
} 