using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Runtime;

/// <summary>
/// Static copy for dashboards: human text + capability tags aligned with IActorRuntimeAdapterFactory runtime ids (InMemory, Orleans, Proto.Actor).
/// </summary>
public static class ActorRuntimeCatalog
{
    /// <summary>Factory/runtime ids: InMemory, Orleans, Proto.Actor.</summary>
    public static IReadOnlyList<ActorRuntimeDescriptor> All { get; } = new[]
    {
        new ActorRuntimeDescriptor(
            Id: "InMemory",
            DisplayName: "In-memory (in-process)",
            Maturity: "supported",
            Summary: "Actors and mailboxes run inside the Host process. Lowest latency and simplest ops for development.",
            Limitations: "No process isolation; state is lost when the Host exits; not suitable for horizontal scale-out.",
            DeploymentNotes: "Default for local dashboards and tests. Single machine only.",
            Capabilities: new[] { "local_dev", "single_process", "low_latency" },
            SupportsProtoRemoting: false),
        new ActorRuntimeDescriptor(
            Id: "Proto.Actor",
            DisplayName: "Proto.Actor",
            Maturity: "experimental",
            Summary: "Uses Proto.Actor with optional remoting endpoints configured from Host settings (host/port).",
            Limitations: "Requires correct remote/cluster setup for multi-node; supported capabilities depend on PRD-020 conformance coverage.",
            DeploymentNotes: "Use Agctor:ProtoHost and Agctor:ProtoPort for remoting. See Host Program.cs initialization.",
            Capabilities: new[] { "remote_messaging", "cluster_ready", "distributed" },
            SupportsProtoRemoting: true),
        new ActorRuntimeDescriptor(
            Id: "Orleans",
            DisplayName: "Orleans (placeholder)",
            Maturity: "placeholder",
            Summary: "Orleans-style grain hosting adapter; maturity depends on implementation in the Agents assembly.",
            Limitations: "May be incomplete relative to InMemory/Proto; verify before production.",
            DeploymentNotes: "Future: silos, clustering, Azure/AWS hosting. Not fully wired in all demos.",
            Capabilities: new[] { "cluster_ready", "cloud_friendly" },
            SupportsProtoRemoting: false)
    };

    /// <summary>Returns a catalog row for the factory id, or null if unknown.</summary>
    public static ActorRuntimeDescriptor? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One row in the actor runtime catalog (PRD-012).</summary>
public sealed record ActorRuntimeDescriptor(
    string Id,
    string DisplayName,
    string Maturity,
    string Summary,
    string Limitations,
    string DeploymentNotes,
    IReadOnlyList<string> Capabilities,
    bool SupportsProtoRemoting);
