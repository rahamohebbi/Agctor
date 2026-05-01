using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Reusable query orchestration for ProjectMemory. This isolates prompt/context
/// construction from pipeline coordination so other entry points can reuse it.
/// </summary>
public sealed class ProjectMemoryQueryService
{
    private readonly ProjectMemoryOperations _ops;
    private readonly IProjectMemoryLlmClient _llm;

    /// <summary>Uses shared search/read + LLM for person-query style answers.</summary>
    public ProjectMemoryQueryService(ProjectMemoryOperations ops, IProjectMemoryLlmClient llm)
    {
        _ops = ops;
        _llm = llm;
    }

    /// <summary>Loads up to 20 entity profiles under <paramref name="entityWorkspaceRoot"/>, builds the query prompt, returns LLM text.</summary>
    public async Task<string> RunAsync(
        AgentDefinitionSpec querySpec,
        string projectRoot,
        string entityWorkspaceRoot,
        string userMessage,
        string? conversationPrefix,
        CancellationToken cancellationToken = default)
    {
        var hits = await _ops.SearchEntitiesAsync(projectRoot, entityWorkspaceRoot, null, cancellationToken).ConfigureAwait(false);
        var sb = new StringBuilder();
        foreach (var h in hits.Take(20))
        {
            var profile = await _ops.ReadDocumentAsync(querySpec, projectRoot, entityWorkspaceRoot, $"people/{h.EntityKey}/profile.md", cancellationToken)
                .ConfigureAwait(false);
            sb.AppendLine($"### {h.EntityKey}");
            sb.AppendLine(profile);
            sb.AppendLine();
        }

        var prompt = ProjectMemoryPromptBuilder.BuildQueryPrompt(querySpec, sb.ToString(), userMessage, conversationPrefix);
        return await _llm.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
    }
}
