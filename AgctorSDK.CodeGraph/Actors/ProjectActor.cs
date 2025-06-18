namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Represents a project within a solution. Contains <see cref="FileActor"/> children.
    /// </summary>
    public sealed class ProjectActor : CodeGraphActorBase
    {
        public ProjectActor(string name, string projectPath) : base(name, projectPath)
        {
        }

        public void AddFile(FileActor file) => AddChild(file);
    }
} 