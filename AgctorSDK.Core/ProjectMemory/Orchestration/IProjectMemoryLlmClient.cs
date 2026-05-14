using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Non-streaming text generation for project-memory pipelines (extract/query), persona playground, and scenario flow router.
/// Host registers <see cref="Ollama.OllamaConfiguredCompletionClient"/> (shared Ollama <c>/api/generate</c> stack with LLMAgent); tests use fakes.
/// </summary>
public interface IProjectMemoryLlmClient
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}
