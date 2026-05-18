using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.Tools.Implementations;

/// <summary>
/// Read-only: loads scenario-scoped people markdown for Q&amp;A (same data as the person-query appendix builder).
/// Invoked via <see cref="ToolRequest"/> from HTTP or scenario flow when <c>person-memory-context</c> is allowed.
/// </summary>
[AgctorHostTool(
    "person-memory-context",
    "Person memory context",
    "Read-only: loads scenario-scoped people markdown for person-query (BuildContext operation).",
    DefaultOperation = "BuildContext")]
public sealed class PersonMemoryContextTool : ToolActorBase
{
    public PersonMemoryContextTool(string id) : base(id, nameof(PersonMemoryContextTool))
    {
    }

    protected override Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(new ToolResult
        {
            IsSuccess = false,
            Error = "PersonMemoryContextTool expects a ToolRequest with Operation BuildContext and parameters."
        });

    public override async Task<ToolResult> Handle(ToolRequest request)
    {
        if (!string.Equals(request.Operation, "BuildContext", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" };
        }

        try
        {
            var p = request.Parameters ?? new Dictionary<string, object>();
            var projectRoot = GetString(p, "projectRoot");
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                try
                {
                    projectRoot = ProjectMemoryServiceAccessor.GetRequiredService<IOptions<ProjectMemoryAgentOptions>>()
                        .Value.ProjectRoot;
                }
                catch (InvalidOperationException)
                {
                    return new ToolResult { IsSuccess = false, Error = "projectRoot parameter required when DI is not initialized." };
                }
            }

            projectRoot = Path.GetFullPath(projectRoot.Trim());
            var scenarioId = GetString(p, "scenarioId");
            var strategy = GetString(p, "contextStrategy") ?? "markdown_all";
            var userMessage = GetString(p, "userMessage") ?? "";
            var agentSpecId = GetString(p, "agentSpecId") ?? "person-query";

            var loader = ProjectMemoryServiceAccessor.GetRequiredService<IProjectLoader>();
            var ops = ProjectMemoryServiceAccessor.GetRequiredService<ProjectMemoryOperations>();
            var ctx = await loader.LoadAsync(projectRoot, CancellationToken.None).ConfigureAwait(false);
            var spec = ctx.AgentSpecs.FirstOrDefault(a =>
                string.Equals(a.Id, agentSpecId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (spec == null)
                return new ToolResult { IsSuccess = false, Error = $"Agent spec '{agentSpecId}' not found in project." };

            var provenance = string.Equals(spec.Role, "coaching", StringComparison.OrdinalIgnoreCase)
                ? "Relationship-coach context: markdown below was loaded via PersonMemoryContextTool (read-only)."
                : "Person-query context: markdown below was loaded via PersonMemoryContextTool (read-only).";
            var appendix = await PersonMemoryMarkdownContextBuilder.BuildAppendixAsync(
                    ops,
                    spec,
                    projectRoot,
                    string.IsNullOrWhiteSpace(scenarioId) ? null : scenarioId.Trim(),
                    strategy,
                    userMessage,
                    CancellationToken.None,
                    provenance)
                .ConfigureAwait(false);

            return new ToolResult { IsSuccess = true, Output = appendix };
        }
        catch (Exception ex)
        {
            return new ToolResult { IsSuccess = false, Error = ex.Message };
        }
    }

    private static string? GetString(IDictionary<string, object> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value == null)
            return null;
        return value switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je => je.ToString(),
            _ => value.ToString()
        };
    }
}
