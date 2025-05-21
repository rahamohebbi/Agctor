using System.Collections.Concurrent;

namespace AGCTOR.ActorModels;

public class InMemoryRuntime : IActorRuntime
{
    public Task<string> SpawnAsync<TAgent>(string name) where TAgent : IAgent, new()
    {
        throw new NotImplementedException();
    }

    public Task SendAsync(string target, object message)
    {
        throw new NotImplementedException();
    }

    public Task SendAsync(string target, object message, string sender)
    {
        throw new NotImplementedException();
    }

    public Task StopAsync(string target)
    {
        throw new NotImplementedException();
    }
}