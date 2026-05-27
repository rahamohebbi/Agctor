using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.Tools.Implementations;

/// <summary>
/// LLM focus picker for playground/pipeline pre-flow. Invoked by <see cref="ProjectMemoryCoreferenceCoordinator"/>,
/// not by persona LLM turns (keeps routing deterministic and lightweight).
/// </summary>
[AgctorHostTool(
    "focus-subject",
    "Focus subject",
    "Resolves who the current message is mainly about (Resolve operation).",
    DefaultOperation = "Resolve")]
public sealed class FocusSubjectTool : ToolActorBase
{
    public FocusSubjectTool(string id) : base(id, nameof(FocusSubjectTool))
    {
    }

    protected override Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(new ToolResult
        {
            IsSuccess = false,
            Error = "FocusSubjectTool expects a ToolRequest with Operation Resolve."
        });

    public override async Task<ToolResult> Handle(ToolRequest request)
    {
        if (!string.Equals(request.Operation, "Resolve", StringComparison.OrdinalIgnoreCase))
            return new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" };

        try
        {
            var p = request.Parameters ?? new Dictionary<string, object>();
            var projectRoot = ResolveProjectRoot(p);
            if (string.IsNullOrWhiteSpace(projectRoot))
                return new ToolResult { IsSuccess = false, Error = "projectRoot is required." };

            projectRoot = Path.GetFullPath(projectRoot.Trim());
            var scenarioId = GetString(p, "scenarioId");
            var userMessage = GetString(p, "userMessage") ?? "";
            var prefix = GetString(p, "conversationPrefix");
            var currentFocus = GetString(p, "currentFocusEntityKey");

            var resolver = ProjectMemoryServiceAccessor.GetRequiredService<IFocusSubjectResolver>();
            var known = await LoadKnownEntitiesAsync(projectRoot, scenarioId, CancellationToken.None).ConfigureAwait(false);
            var result = await resolver
                .ResolveAsync(
                    new FocusSubjectRequest
                    {
                        UserMessage = userMessage,
                        ConversationPrefix = prefix,
                        CurrentFocusEntityKey = currentFocus,
                        KnownEntities = known
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            return new ToolResult
            {
                IsSuccess = true,
                Output = JsonSerializer.Serialize(new
                {
                    entityKey = result.EntityKey,
                    displayName = result.DisplayName,
                    changedFromCurrent = result.ChangedFromCurrent,
                    reason = result.Reason
                })
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { IsSuccess = false, Error = ex.Message };
        }
    }

    private static async Task<IReadOnlyList<KnownEntity>> LoadKnownEntitiesAsync(
        string projectRoot,
        string? scenarioId,
        CancellationToken cancellationToken)
    {
        var loader = ProjectMemoryServiceAccessor.GetRequiredService<IProjectLoader>();
        var entities = ProjectMemoryServiceAccessor.GetRequiredService<IEntityRegistry>();
        var workspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRoot, scenarioId);
        var ctx = await loader.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var discovered = await entities.DiscoverAsync(ctx, workspace, cancellationToken).ConfigureAwait(false);
        return discovered
            .Select(e => new KnownEntity
            {
                EntityKey = e.EntityKey,
                DisplayName = e.Metadata?.DisplayName ?? e.EntityKey,
                Aliases = e.Metadata?.Aliases
            })
            .ToList();
    }

    private static string? ResolveProjectRoot(IDictionary<string, object> p)
    {
        var root = GetString(p, "projectRoot");
        if (!string.IsNullOrWhiteSpace(root))
            return root;
        try
        {
            return ProjectMemoryServiceAccessor.GetRequiredService<IOptions<ProjectMemoryAgentOptions>>().Value.ProjectRoot;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? GetString(IDictionary<string, object> p, string key)
    {
        if (!p.TryGetValue(key, out var raw) || raw == null)
            return null;
        return Convert.ToString(raw)?.Trim();
    }
}
