using AgctorSDK.Orleans.Contracts;
using Orleans.Runtime;

namespace AgctorSDK.OrleansSilo.Grains;

/// <inheritdoc />
public sealed class AgctorHealthGrain : Grain, IAgctorHealthGrain
{
    public Task<string> PingAsync() => Task.FromResult("ok");
}
