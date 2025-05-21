namespace AGCTOR.ActorModels;

public interface IActorRuntime
{
    Task<string> SpawnAsync<TAgent>(string name) where TAgent : IAgent, new();
    Task SendAsync(string target, object message);
    Task SendAsync(string target, object message, string sender); // NEW
    Task StopAsync(string target);
}