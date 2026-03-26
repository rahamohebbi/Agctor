using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Maps live <see cref="IActorRuntimeAdapter"/> instances to factory ids (InMemory, Orleans, Proto.Actor).
/// InMemory reports Name "InMemoryActorRuntime" but the factory key is InMemory.
/// </summary>
public static class RuntimeCanonicalId
{
    public static string FromAdapter(IActorRuntimeAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return adapter switch
        {
            InMemoryActorRuntime => "InMemory",
            ProtoActorAdapter => "Proto.Actor",
            OrleansAdapter => "Orleans",
            _ => adapter.Name
        };
    }
}
