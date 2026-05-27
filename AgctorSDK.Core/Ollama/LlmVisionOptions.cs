using System;

namespace AgctorSDK.Core.Ollama;

/// <summary>Ollama Gemma 4 vision settings (PRD-023), bound from <c>Agctor:LLM</c>.</summary>
public sealed class LlmVisionOptions
{
  public string? VisionModel { get; set; }

  public string[] VisionFallbackModels { get; set; } = Array.Empty<string>();

  public int VisionTimeoutSeconds { get; set; } = 300;

  public int MaxVisualEdgePixels { get; set; } = 1024;

  public int VisualTokenBudget { get; set; } = 280;

  public string ExtractPromptVersion { get; set; } = "visual-extract-v1";
}
