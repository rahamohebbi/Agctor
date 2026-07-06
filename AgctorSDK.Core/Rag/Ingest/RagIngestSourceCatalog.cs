namespace AgctorSDK.Core.Rag.Ingest;

/// <summary>Static ingest source catalog for dashboard (implemented + planned).</summary>
public static class RagIngestSourceCatalog
{
    public static IReadOnlyList<RagIngestSourceDescriptor> All { get; } =
    [
        new(
            RagIngestSourceIds.AgctorMarkdown,
            "Agctor markdown",
            "Indexes Project Memory markdown: `.agctor/**/*.md` templates/docs and `scenarios/*/people/**/*.md` entity files.",
            IsImplemented: true),
        new(
            RagIngestSourceIds.PdfDocument,
            "PDF documents",
            "Upload or point at PDF files for chunking and indexing. Planned — not available yet.",
            IsImplemented: false),
        new(
            RagIngestSourceIds.WorkspaceFolder,
            "Workspace folder",
            "Ingest an arbitrary project folder (code, notes, mixed files). Planned — not available yet.",
            IsImplemented: false)
    ];
}
