namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Represents a class (or similar top-level type) inside a source file. Contains <see cref="MethodActor"/> children.
    /// </summary>
    public sealed class ClassActor : CodeGraphActorBase
    {
        /// <summary>
        /// Approximate number of lines of code contained in this class, including whitespace and comments.
        /// </summary>
        public int? LinesOfCode { get; set; }

        public ClassActor(string name) : base(name)
        {
        }

        public void AddMethod(MethodActor method) => AddChild(method);
    }
} 