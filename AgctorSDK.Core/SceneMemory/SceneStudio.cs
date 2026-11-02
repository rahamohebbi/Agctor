using System.Collections.Generic;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Opaque, case-sensitive IDs scoped to an external application's project and story. Not authorization credentials.</summary>
public sealed record SceneWorkspaceConnection(string Application, string ProjectId, string StoryId);

/// <summary>A Studio asset may have multiple distinct instances in a story (for example two matching swords).</summary>
public sealed record SceneStudioBinding(string InstanceKey, string AssetId, long Version, string DisplayName,
    IReadOnlyDictionary<string, string> Defaults);

/// <summary>Version is monotonic per instance. EntityId explicitly links a discovered identity; names never implicitly merge identities.</summary>
public sealed record SceneStudioInput(string AssetId, long Version, string Name, string Kind,
    IReadOnlyDictionary<string, string>? Defaults = null, string? EntityId = null);
