namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Represents a class (or similar top-level type) inside a source file. Contains <see cref="MethodActor"/> children.
    /// </summary>
    public sealed class ClassActor : CodeGraphActorBase
    {
        public ClassActor(string name) : base(name)
        {
        }

        public void AddMethod(MethodActor method) => AddChild(method);
    }
} 