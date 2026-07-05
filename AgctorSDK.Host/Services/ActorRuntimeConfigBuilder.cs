using AgctorSDK.Core.Runtime;

namespace AgctorSDK.Host.Services;

/// <summary>Builds the config dictionary passed to <see cref="AgctorSDK.Core.Interfaces.IActorRuntimeAdapter.InitializeAsync"/>.</summary>
public static class ActorRuntimeConfigBuilder
{
    public static Dictionary<string, object> FromConfiguration(
        IConfiguration configuration,
        string? runtimeName = null,
        string? environmentName = null)
    {
        // Explicit runtime id avoids stale DefaultRuntime during hot-swap right after appsettings.User.json write.
        var resolved = AgctorRuntimeCatalog.NormalizeRuntimeName(
            runtimeName ?? configuration.GetValue<string>("Agctor:DefaultRuntime")) ?? AgctorRuntimeCatalog.InMemory;

        var dict = new Dictionary<string, object>
        {
            ["Environment"] = environmentName ?? "Production",
            ["MaxConcurrentMessages"] = 1000,
            ["DefaultTimeoutMs"] = 30000
        };

        if (string.Equals(resolved, AgctorRuntimeCatalog.ProtoActor, StringComparison.OrdinalIgnoreCase))
        {
            dict["remoteHost"] = configuration.GetValue<string>("Agctor:ProtoHost", "127.0.0.1") ?? "127.0.0.1";
            dict["remotePort"] = configuration.GetValue("Agctor:ProtoPort", 12000);
        }

        if (string.Equals(resolved, AgctorRuntimeCatalog.Orleans, StringComparison.OrdinalIgnoreCase))
        {
            dict["clusterId"] = configuration.GetValue<string>("Agctor:OrleansClusterId", "agctor-dev") ?? "agctor-dev";
            dict["serviceId"] = configuration.GetValue<string>("Agctor:OrleansServiceId", "agctor-host") ?? "agctor-host";
            dict["gatewayHost"] = configuration.GetValue<string>("Agctor:OrleansGatewayHost", "127.0.0.1") ?? "127.0.0.1";
            dict["gatewayPort"] = configuration.GetValue("Agctor:OrleansGatewayPort", 30000);
        }

        return dict;
    }
}
