using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Runtime;

/// <summary>
/// Describes one configurable field shown in the actor-runtime dashboard for a backend.
/// </summary>
public sealed record RuntimeConfigField(
    string Key,
    string Label,
    string FieldType,
    string? DefaultValue,
    string? Placeholder,
    string? HelpText,
    bool Required = false);

/// <summary>
/// Per-runtime configuration fields and Docker metadata for the dashboard (PRD-012+).
/// </summary>
public static class ActorRuntimeConfigSchema
{
    /// <summary>Factory ids that can run a local Docker sidecar via docker compose.</summary>
    public static IReadOnlySet<string> DockerBackedRuntimes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Orleans", "Proto.Actor" };

    /// <summary>Docker Compose service name for a runtime id.</summary>
    public static string? GetDockerServiceName(string? runtimeId) => runtimeId switch
    {
        "Orleans" => "orleans-silo",
        "Proto.Actor" => "proto-actor-node",
        _ => null
    };

    /// <summary>Dashboard form fields persisted under Agctor:* keys.</summary>
    public static IReadOnlyList<RuntimeConfigField> GetFields(string? runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return Array.Empty<RuntimeConfigField>();

        return runtimeId switch
        {
            "InMemory" => Array.Empty<RuntimeConfigField>(),
            "Proto.Actor" =>
            [
                new("ProtoHost", "Remoting host", "text", "127.0.0.1", "127.0.0.1",
                    "Host address the Host binds or connects to for Proto gRPC remoting."),
                new("ProtoPort", "Remoting port", "number", "12000", "12000",
                    "TCP port for Proto.Actor gRPC remoting.", Required: true)
            ],
            "Orleans" =>
            [
                new("OrleansClusterId", "Cluster ID", "text", "agctor-dev", "agctor-dev",
                    "Orleans cluster identifier; must match the silo container."),
                new("OrleansServiceId", "Service ID", "text", "agctor-host", "agctor-host",
                    "Orleans service identifier shared by silo and client."),
                new("OrleansGatewayHost", "Gateway host", "text", "127.0.0.1", "127.0.0.1",
                    "Host where the Orleans silo gateway listens."),
                new("OrleansGatewayPort", "Gateway port", "number", "30000", "30000",
                    "Orleans client gateway port (default 30000).", Required: true),
                new("AllowExperimentalRuntimes", "Allow experimental runtimes", "checkbox", "true", null,
                    "Required to load Proto.Actor or Orleans instead of falling back to InMemory.")
            ],
            _ => Array.Empty<RuntimeConfigField>()
        };
    }
}
