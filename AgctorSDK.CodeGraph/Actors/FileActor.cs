namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Represents a source code file within a project. Contains <see cref="ClassActor"/> children.
    /// </summary>
    public sealed class FileActor : CodeGraphActorBase
    {
        public FileActor(string name, string filePath) : base(name, filePath)
        {
        }

        public void AddClass(ClassActor @class) => AddChild(@class);
    }
} 