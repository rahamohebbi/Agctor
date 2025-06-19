namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Represents a method/function inside a class.
    /// Leaf node in Stage-1 hierarchy.
    /// </summary>
    public sealed class MethodActor : CodeGraphActorBase
    {
        /// <summary>
        /// Approximate number of lines of code contained in this method, including whitespace and comments.
        /// </summary>
        public int? LinesOfCode { get; set; }

        public MethodActor(string name) : base(name)
        {
        }
    }
} 