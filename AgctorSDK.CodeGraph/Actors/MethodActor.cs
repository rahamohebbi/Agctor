namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Represents a method/function inside a class.
    /// Leaf node in Stage-1 hierarchy.
    /// </summary>
    public sealed class MethodActor : CodeGraphActorBase
    {
        public MethodActor(string name) : base(name)
        {
        }
    }
} 