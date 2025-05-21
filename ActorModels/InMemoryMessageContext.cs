namespace AGCTOR.ActorModels;

public class InMemoryMessageContext : IMessageContext
{
    private readonly InMemoryRuntime _runtime;

    public string Sender { get; set; }
    public string Receiver { get; set; }
    public object Message { get; set; }

    private TaskCompletionSource<object> _responseTcs;

    public InMemoryMessageContext(InMemoryRuntime runtime)
    {
        _runtime = runtime;
        _responseTcs = new TaskCompletionSource<object>();
    }

    public Task RespondAsync(object response)
    {
        _responseTcs.TrySetResult(response);
        return Task.CompletedTask;
    }

    public Task SendAsync(string target, object message, string sender)
    {
        return _runtime.SendAsync(target, message, Sender);
    }

    public Task<object> WaitForResponseAsync()
    {
        return _responseTcs.Task;
    }
}