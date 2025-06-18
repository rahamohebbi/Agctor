using System.IO;
using System.Threading.Tasks;

namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Root actor that represents a development solution (.sln). Contains <see cref="ProjectActor"/> children.
    /// </summary>
    public sealed class SolutionActor : CodeGraphActorBase
    {
        public SolutionActor(string name, string solutionPath) : base(name, solutionPath)
        {
        }

        /// <summary>
        /// Convenience method for adding a <see cref="ProjectActor"/>.
        /// </summary>
        public void AddProject(ProjectActor project) => AddChild(project);

        #region Persistence helpers
        private const string DefaultFileName = "solution.json";

        /// <summary>
        /// Saves the actor hierarchy rooted at this solution to the specified directory.
        /// A single JSON file (<c>solution.json</c>) is emitted for Stage-1 simplicity.
        /// </summary>
        public async Task SaveStateAsync(string directory)
        {
            var dto = Persistence.ActorSerializer.ToDto(this);
            Directory.CreateDirectory(directory);
            var jsonPath = Path.Combine(directory, DefaultFileName);
            await Persistence.ActorSerializer.WriteAsync(dto, jsonPath);
        }

        /// <summary>
        /// Loads a <see cref="SolutionActor"/> hierarchy from a previously-saved directory.
        /// </summary>
        /// <param name="directory">Directory that contains <c>solution.json</c>.</param>
        public static async Task<SolutionActor> LoadStateAsync(string directory)
        {
            var jsonPath = Path.Combine(directory, DefaultFileName);
            var dto = await Persistence.ActorSerializer.ReadAsync(jsonPath);
            return (SolutionActor)Persistence.ActorSerializer.FromDto(dto);
        }
        #endregion
    }
} 