namespace AgctorSDK.Host.Models;

/// <summary>Request body for <c>PUT /api/Llm/default-model</c> (PRD-015).</summary>
public sealed class LlmDefaultModelRequest
{
    /// <summary>Ollama model id (e.g. <c>llama3:latest</c>).</summary>
    public string Model { get; set; } = "";
}

/// <summary>Wrapped list for JSON consistency with other Host APIs.</summary>
public sealed class LlmModelsResponse
{
    public IReadOnlyList<LlmModelItemDto> Models { get; init; } = Array.Empty<LlmModelItemDto>();
}

public sealed class LlmModelItemDto
{
    public string Name { get; init; } = "";
    public long? Size { get; init; }
    public string? ModifiedAt { get; init; }
}

/// <summary>Result of applying a new default; optional warning when catalog verify is suspicious.</summary>
public sealed class SetLlmDefaultModelResponse
{
    public string? Warning { get; init; }
}
