using System;
using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Visual.Models;

namespace AgctorSDK.Core.ProjectMemory.Visual.Actors;

public sealed class VisualIngestEnrichRequest
{
    public required string ProjectRoot { get; init; }

    public required string ScenarioId { get; init; }

    public required string AssetId { get; init; }
}

public sealed class VisualIngestEnrichResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }
}

public sealed class VisualInferRequest
{
    public required string ProjectRoot { get; init; }

    public required string ScenarioId { get; init; }

    public required string AssetId { get; init; }

    public string? UserMessage { get; init; }

    public string? FocusEntityKey { get; init; }
}

public sealed class VisualInferResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public bool Skipped { get; init; }

    public string? ModelUsed { get; init; }

    public VisualAssetRecord? Record { get; init; }
}

public sealed class VisualExtractRequest
{
    public required string ProjectRoot { get; init; }

    public required string ScenarioId { get; init; }

    public required string AssetId { get; init; }

    public string? UserMessage { get; init; }

    public string? FocusEntityKey { get; init; }

    public bool ReExtract { get; init; }
}

public sealed class VisualExtractResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public bool Skipped { get; init; }

    public string? ModelUsed { get; init; }

    public int IntentCount { get; init; }

    public int ProposalCount { get; init; }

    public int RoutedCount { get; init; }

    public VisualAssetRecord? Record { get; init; }

    public IReadOnlyList<OutOfSchemaFactProposal> Proposals { get; init; } =
        Array.Empty<OutOfSchemaFactProposal>();
}
