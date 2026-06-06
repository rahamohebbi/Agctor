using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Host.Models;

public sealed class ProjectMemoryStatusDto
{
    public string ProjectRoot { get; set; } = "";
    /// <summary>Repo sample path when <c>Agctor:ProjectMemory:ProjectRoot</c> is unset (Host default: <c>samples/people-project</c>).</summary>
    public string DefaultSampleProjectRoot { get; set; } = "";
    /// <summary>True when <see cref="ProjectRoot"/> equals the built-in <c>samples/people-project</c> default.</summary>
    public bool UsesDefaultSampleProjectRoot { get; set; }
    public bool ProjectLoaded { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectType { get; set; }
    public string? RuntimeMode { get; set; }
    public int AgentCount { get; set; }
    public string? Error { get; set; }
}

/// <summary>One path from <c>git status --porcelain</c> under the active project root.</summary>
public sealed class WorkspaceGitChangeItemDto
{
    /// <summary>First two status columns (index + work tree), e.g. <c>M </c> or <c>??</c>.</summary>
    public string Status { get; set; } = "";
    /// <summary>Path relative to <see cref="ProjectMemoryStatusDto.ProjectRoot"/> using forward slashes.</summary>
    public string RelativePath { get; set; } = "";
}

/// <summary>Git working tree changes limited to the configured portable project folder.</summary>
public sealed class WorkspaceGitChangesDto
{
    public bool GitAvailable { get; set; }
    public string? GitRoot { get; set; }
    public string? Message { get; set; }
    public IReadOnlyList<WorkspaceGitChangeItemDto> Files { get; set; } = Array.Empty<WorkspaceGitChangeItemDto>();
}

public sealed class AgentListItemDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";

    /// <summary>YAML <c>description</c> — LlmNode inspector and agent list context.</summary>
    public string Description { get; set; } = "";

    /// <summary>YAML <c>input.type</c>.</summary>
    public string InputType { get; set; } = "";

    /// <summary>YAML <c>output.type</c>.</summary>
    public string OutputType { get; set; } = "";

    /// <summary>YAML <c>tools.allow</c> (declared; flow LlmNode is primarily one LLM step unless host adds ingest).</summary>
    public IReadOnlyList<string> ToolsAllow { get; set; } = Array.Empty<string>();

    /// <summary>YAML <c>tools.deny</c>.</summary>
    public IReadOnlyList<string> ToolsDeny { get; set; } = Array.Empty<string>();

    /// <summary>YAML <c>memoryAccess.read</c> patterns.</summary>
    public IReadOnlyList<string> MemoryRead { get; set; } = Array.Empty<string>();

    /// <summary>YAML <c>memoryAccess.write</c> patterns.</summary>
    public IReadOnlyList<string> MemoryWrite { get; set; } = Array.Empty<string>();

    /// <summary>YAML <c>guardrails</c>.</summary>
    public IReadOnlyList<string> Guardrails { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ProjectTypes { get; set; } = Array.Empty<string>();
    public string? SourcePath { get; set; }
    public string? RelativePath { get; set; }
}

public sealed class AgentDetailDto
{
    public AgentDefinitionSpec Spec { get; set; } = new();
    public string? RelativePath { get; set; }
    public string? YamlPreview { get; set; }
}

public sealed class SaveAgentRequestDto
{
    public AgentDefinitionSpec? Spec { get; set; }
    /// <summary>If creating a new file, optional relative path; default <c>.agctor/agents/people/{id}.agent.yaml</c>.</summary>
    public string? RelativePath { get; set; }
}

public sealed class AgentTemplateDto
{
    public string TemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public AgentDefinitionSpec Spec { get; set; } = new();
}

public sealed class CreateAgentFromTemplateRequestDto
{
    public string TemplateId { get; set; } = "";
    public string NewId { get; set; } = "";
    /// <summary>e.g. <c>people</c> or <c>shared</c> — folder under <c>.agctor/agents/</c>.</summary>
    public string AgentsSubfolder { get; set; } = "people";
}

public sealed class SchemaBundleResponseDto
{
    public IReadOnlyDictionary<string, SchemaFileDto> Files { get; set; } =
        new Dictionary<string, SchemaFileDto>(StringComparer.OrdinalIgnoreCase);
}

public sealed class SchemaFileDto
{
    public string Segment { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Yaml { get; set; } = "";
}

public sealed class SaveSchemaSegmentRequestDto
{
    public string Yaml { get; set; } = "";
}

public sealed class ValidateResponseDto
{
    public bool Success { get; set; }
    public IReadOnlyList<ValidationIssueDto> Issues { get; set; } = Array.Empty<ValidationIssueDto>();
}

public sealed class ValidationIssueDto
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Path { get; set; }
    public bool IsError { get; set; }
}

public sealed class RebuildResponseDto
{
    public bool Success { get; set; }
    public string? LogPath { get; set; }
    public IReadOnlyList<ValidationIssueDto> Issues { get; set; } = Array.Empty<ValidationIssueDto>();
}

public sealed class SetProjectRootRequestDto
{
    public string ProjectRoot { get; set; } = "";
}

public sealed class TreeNodeDto
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public List<TreeNodeDto> Children { get; set; } = new();
}

public sealed class FilePreviewDto
{
    public string RelativePath { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Truncated { get; set; }
}

public sealed class ProjectMemoryPlaygroundRunRequestDto
{
    /// <summary>Optional — when set, prior transcript turns are included in the prompt (non-streaming one-shot).</summary>
    public string? SessionId { get; set; }

    /// <summary>Optional scenario id for persona path hint (<c>scenarios/&lt;id&gt;/people/</c>).</summary>
    public string? ScenarioId { get; set; }

    public string AgentId { get; set; } = "";
    public string InputText { get; set; } = "";
}

/// <summary>Attachment reference on a playground stream request (PRD-023b).</summary>
public sealed class PlaygroundStreamAttachmentDto
{
    public string AssetId { get; set; } = "";

    public string State { get; set; } = "uploaded";

    public string? FileName { get; set; }

    public string? Mime { get; set; }

    /// <summary>Optional primary subject from Tag popover (applied before vision infer).</summary>
    public string? EntityKey { get; set; }

    /// <summary>Optional second person in the photo (role <c>also_in_photo</c>).</summary>
    public string? SecondaryEntityKey { get; set; }

    /// <summary>User caption from Tag popover (distinct from chat message text).</summary>
    public string? Caption { get; set; }

    /// <summary>Privacy sensitivity: <c>normal</c>, <c>sensitive</c>, or <c>do_not_infer</c>.</summary>
    public string? Sensitivity { get; set; }
}

/// <summary>Body for SSE chat turns (same session store as CodeGraph chat).</summary>
public sealed class ProjectMemoryPlaygroundStreamRequestDto
{
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Payload { get; set; } = "";

    /// <summary>Optional scenario id for persona path hint in streamed prompts.</summary>
    public string? ScenarioId { get; set; }

    /// <summary>Client-generated id grouping user + assistant turns; server generates if empty.</summary>
    public string? TurnGroupId { get; set; }

    /// <summary>Uploaded visual assets (state <c>uploaded</c> is enough — subjects inferred later).</summary>
    public List<PlaygroundStreamAttachmentDto>? Attachments { get; set; }
}

public sealed class ProjectMemoryPlaygroundRunResponseDto
{
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string OutputText { get; set; } = "";
    public bool OutputLooksLikeJson { get; set; }
    public string? JsonValidationError { get; set; }
    public long ElapsedMs { get; set; }
}

/// <summary>PRD-013 multi-step pipeline: extract → route/write → optional query.</summary>
public sealed class ProjectMemoryOrchestratorRunRequestDto
{
    /// <summary>Optional — prior turns included in extract/query prompts.</summary>
    public string? SessionId { get; set; }

    public string UserMessage { get; set; } = "";

    /// <summary>Client-supplied id for tracing; server generates one if empty.</summary>
    public string? CorrelationId { get; set; }

    /// <summary><c>auto</c> (default), <c>ingestOnly</c>, or <c>queryOnly</c>.</summary>
    public string Mode { get; set; } = "auto";

    /// <summary>When set, reads/writes <c>people/</c> under <c>scenarios/&lt;sanitized&gt;/</c>.</summary>
    public string? ScenarioId { get; set; }
}

public sealed class ProjectMemoryOrchestratorRunResponseDto
{
    public string CorrelationId { get; set; } = "";
    public bool Success { get; set; }
    public string FinalText { get; set; } = "";
    public IReadOnlyList<ProjectMemoryOrchestratorStepDto> Steps { get; set; } = Array.Empty<ProjectMemoryOrchestratorStepDto>();
}

public sealed class ProjectMemoryOrchestratorStepDto
{
    public string Name { get; set; } = "";
    public bool Ok { get; set; }
    public string? Detail { get; set; }
    public IReadOnlyList<string>? UpdatedFiles { get; set; }
}

/// <summary>Caller knobs for <c>POST /api/project-memory/generic-inbox/replay</c> (PRD-019 back-fill).</summary>
public sealed class ProjectMemoryGenericInboxReplayRequestDto
{
    /// <summary>When set, only confirmed rows tagged with this sanitized scenario segment are replayed.</summary>
    public string? ScenarioId { get; set; }

    /// <summary>When true, includes rows that were already replayed previously.</summary>
    public bool IncludeAlreadyReplayed { get; set; }

    /// <summary>Optional whitelist of entity keys (e.g. <c>raha</c>).</summary>
    public IReadOnlyList<string>? OnlyEntityKeys { get; set; }

    /// <summary>Optional whitelist of knowledge types.</summary>
    public IReadOnlyList<string>? OnlyKnowledgeTypes { get; set; }
}

public sealed class ProjectMemoryGenericInboxReplayResponseDto
{
    public int Considered { get; set; }
    public int Routed { get; set; }
    public int SkippedAlreadyReplayed { get; set; }
    public int SkippedRouteMiss { get; set; }
    public int SkippedUnresolvedEntity { get; set; }
    public IReadOnlyList<string> UpdatedFiles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<ProjectMemoryGenericInboxReplayIssueDto> Issues { get; set; } = Array.Empty<ProjectMemoryGenericInboxReplayIssueDto>();
}

public sealed class ProjectMemoryGenericInboxReplayIssueDto
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsError { get; set; }
}

/// <summary>PRD-022a: one pending generic-inbox row for the confirmation UI.</summary>
public sealed class GenericInboxPendingItemDto
{
    public string ProposalId { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public string KnowledgeType { get; set; } = "";
    public string? Attribute { get; set; }
    public string Value { get; set; } = "";
    public double Confidence { get; set; }
    public string Disposition { get; set; } = "";
    public string ScenarioSegment { get; set; } = "";
    public string QueuedAtUtc { get; set; } = "";
    public string UserPromptLine { get; set; } = "";

    /// <summary>When set, playground inbox UI can show the related photo thumbnail.</summary>
    public string? SourceAssetId { get; set; }
}

public sealed class GenericInboxPendingListResponseDto
{
    public string ScenarioId { get; set; } = "";
    public IReadOnlyList<GenericInboxPendingItemDto> Items { get; set; } = Array.Empty<GenericInboxPendingItemDto>();
}

public sealed class GenericInboxDecisionItemDto
{
    public string ProposalId { get; set; } = "";
    public bool Approve { get; set; }
}

public sealed class GenericInboxDecideRequestDto
{
    public string? ScenarioId { get; set; }

    /// <summary>PRD-024: session that owns the suspended scenario flow (for inbox.confirmed resume).</summary>
    public string? SessionId { get; set; }

    public IReadOnlyList<GenericInboxDecisionItemDto> Decisions { get; set; } = Array.Empty<GenericInboxDecisionItemDto>();
}

public sealed class GenericInboxDecideResponseDto
{
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int RejectedMismatch { get; set; }
    public IReadOnlyList<string> UpdatedFiles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    /// <summary>When style photo loop refreshes advice after inbox approval.</summary>
    public string? StyleRefreshText { get; set; }
}

/// <summary>PRD-022b companion privacy settings.</summary>
public sealed class CompanionPrivacySettingsDto
{
    public bool AutoIngestOnSessionEnd { get; set; } = true;
}

public sealed class ForgetPersonRequestDto
{
    public string ScenarioId { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public string? ProjectId { get; set; }
    public bool ClearProjectFocusWhenMatched { get; set; } = true;
}

/// <summary>One-line daily-life nudge from <see cref="PersonLifeSignalsReader"/>.</summary>
public sealed class ScenarioEntityListItemDto
{
    public string EntityKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

/// <summary>Playground: infer project focus from name + sync conversation coref store on session open.</summary>
public sealed class PlaygroundSyncFocusRequestDto
{
    public string SessionId { get; set; } = "";
    public string? ProjectId { get; set; }
}

public sealed class PlaygroundSyncFocusResponseDto
{
    public string? FocusEntityKey { get; set; }
    public string? FocusDisplayName { get; set; }
    public bool InferredFromProjectName { get; set; }
    /// <summary>True when focus was loaded from the scenario conversation store (chat-driven focus shift).</summary>
    public bool UpdatedFromConversation { get; set; }
}

public sealed class PersonLifeSignalDto
{
    public string EntityKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public int? DaysUntil { get; set; }
    public int Priority { get; set; }
}

public sealed class PersonLifeSignalsResponseDto
{
    public string ScenarioId { get; set; } = "";
    public IReadOnlyList<PersonLifeSignalDto> Signals { get; set; } = Array.Empty<PersonLifeSignalDto>();
}

/// <summary>Fast path to persist a short note without full chat (runs ingest pipeline).</summary>
public sealed class ProjectMemoryQuickCaptureRequestDto
{
    public string Text { get; set; } = "";
    public string? ScenarioId { get; set; }
    public string? SessionId { get; set; }
}

/// <summary>Flush session transcript into memory via ingest (timeline / profile updates).</summary>
public sealed class ProjectMemorySessionCaptureRequestDto
{
    public string? ScenarioId { get; set; }
}
