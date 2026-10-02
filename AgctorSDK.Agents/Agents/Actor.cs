namespace AgctorSDK.Core.Agents
{
    /// <summary>
    /// Actor base whose decisions go through <see cref="BaseActor.Context"/>.
    /// Subclasses call <c>Decide</c> from a message handler; the runtime selects the engine.
    /// </summary>
    public abstract class Actor : BaseActor
    {
        protected Actor(string id)
            : base(id)
        {
        }

        protected Actor(string id, string actorType)
            : base(id, actorType)
        {
        }
    }
}
