using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Non-streaming text generation for project-memory pipelines (extract/query).
/// Host registers an Ollama-backed implementation; tests use fakes.
/// </summary>
public interface IProjectMemoryLlmClient
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}
