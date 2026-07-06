namespace AgctorSDK.Core.Rag.Ingest;

/// <summary>Stable ids for dashboard ingest source picker (extensible catalog).</summary>
public static class RagIngestSourceIds
{
    /// <summary>Project Memory markdown under <c>.agctor/</c> and <c>scenarios/*/people/</c>.</summary>
    public const string AgctorMarkdown = "agctor_markdown";

    /// <summary>Reserved — PDF upload / folder ingest (not implemented in v1).</summary>
    public const string PdfDocument = "pdf_document";

    /// <summary>Reserved — arbitrary workspace folder (not implemented in v1).</summary>
    public const string WorkspaceFolder = "workspace_folder";

    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return AgctorMarkdown;

        var t = id.Trim();
        if (t.Equals(AgctorMarkdown, StringComparison.OrdinalIgnoreCase)
            || t.Equals("agctor-markdown", StringComparison.OrdinalIgnoreCase))
            return AgctorMarkdown;

        if (t.Equals(PdfDocument, StringComparison.OrdinalIgnoreCase))
            return PdfDocument;

        if (t.Equals(WorkspaceFolder, StringComparison.OrdinalIgnoreCase))
            return WorkspaceFolder;

        return t;
    }
}
