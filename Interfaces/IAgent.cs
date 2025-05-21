namespace AGCTOR.ActorModels;

public interface IAgent
{
    Task OnReceiveAsync(IMessageContext context);
}