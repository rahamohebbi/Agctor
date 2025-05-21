namespace AGCTOR.ActorModels;

public interface IMessageContext
{
    string Sender { get; }
    string Receiver { get; }
    object Message { get; }
    Task RespondAsync(object response);
    
    Task SendAsync(string target, object message, string sender);
}