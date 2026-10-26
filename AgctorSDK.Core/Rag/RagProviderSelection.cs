namespace AgctorSDK.Core.Rag;

/// <summary>A provider choice frozen for a message or suspended flow run.</summary>
public sealed record RagProviderSelection(string ProviderId, string Source)
{
    public static RagProviderSelection Resolve(string? nodeProvider, RagProviderSelection? inherited, string globalProvider) =>
        !string.IsNullOrWhiteSpace(nodeProvider) ? new(RagProviderIds.Normalize(nodeProvider), "flow node")
        : inherited ?? new(RagProviderIds.Normalize(globalProvider), "global default");
}
