using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Host.Models;

public sealed class ProjectMemoryStatusDto
{
    public string ProjectRoot { get; set; } = "";
    public bool ProjectLoaded { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectType { get; set; }
    public string? RuntimeMode { get; set; }
    public int AgentCount { get; set; }
    public string? Error { get; set; }
}

public sealed class AgentListItemDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
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

    public string AgentId { get; set; } = "";
    public string InputText { get; set; } = "";
}

/// <summary>Body for SSE chat turns (same session store as CodeGraph chat).</summary>
public sealed class ProjectMemoryPlaygroundStreamRequestDto
{
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Payload { get; set; } = "";
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
