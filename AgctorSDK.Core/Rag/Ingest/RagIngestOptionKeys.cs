namespace AgctorSDK.Core.Rag.Ingest;

/// <summary>Keys for <see cref="RagIngestSourceContext.Options"/>.</summary>
public static class RagIngestOptionKeys
{
    /// <summary>When true, Cognee ingest re-runs remember/cognify even if the dataset already exists.</summary>
    public const string ForceReingest = "forceReingest";
}
