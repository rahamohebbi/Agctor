using Orleans;

namespace AgctorSDK.Orleans.Contracts;

/// <summary>
/// Shared health grain contract between the Docker silo and Host Orleans client.
/// </summary>
public interface IAgctorHealthGrain : IGrainWithIntegerKey
{
    Task<string> PingAsync();
}
