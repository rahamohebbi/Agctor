using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Tools.Implementations;

/// <summary>
/// Read-only visual retrieval for coach/query personas (PRD-023c).
/// </summary>
[AgctorHostTool(
    "person-visual-context",
    "Person visual context",
    "Loads scenario photo catalog for LLM context (BuildContext, RetrieveByIntent, ListForEntity).",
    DefaultOperation = "BuildContext")]
public sealed class PersonVisualContextTool : ToolActorBase
{
    public PersonVisualContextTool(string id) : base(id, nameof(PersonVisualContextTool))
    {
    }

    protected override Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(new ToolResult
        {
            IsSuccess = false,
            Error = "PersonVisualContextTool expects a ToolRequest with a supported Operation."
        });

    public override async Task<ToolResult> Handle(ToolRequest request)
    {
        var op = request.Operation?.Trim() ?? "";
        var p = request.Parameters ?? new Dictionary<string, object>();

        try
        {
            var builder = ProjectMemoryServiceAccessor.GetRequiredService<PersonVisualContextBuilder>();
            var root = VisualToolParams.ResolveProjectRoot(p);
            var scenarioId = VisualToolParams.RequireScenarioId(p);

            switch (op.ToLowerInvariant())
            {
                case "buildcontext":
                {
                    var intent = VisualToolParams.GetString(p, "visualIntent") ?? "general";
                    var userMessage = VisualToolParams.GetString(p, "userMessage") ?? "";
                    var maxAssets = VisualToolParams.GetInt32(p, "maxAssets", 3);
                    var entityKeys = ParseEntityKeys(p);
                    var assetIds = ParseAssetIds(p);
                    var result = await builder
                        .BuildAsync(root, scenarioId, userMessage, intent, entityKeys, maxAssets, CancellationToken.None, assetIds)
                        .ConfigureAwait(false);
                    return OkContext(result);
                }
                case "retrievebyintent":
                {
                    var intent = VisualToolParams.GetString(p, "visualIntent") ?? "general";
                    var userMessage = VisualToolParams.GetString(p, "userMessage") ?? "";
                    var maxAssets = VisualToolParams.GetInt32(p, "maxAssets", 3);
                    var result = await builder
                        .BuildAsync(root, scenarioId, userMessage, intent, null, maxAssets, CancellationToken.None)
                        .ConfigureAwait(false);
                    return OkContext(result);
                }
                case "listforentity":
                {
                    var entityKey = VisualToolParams.GetString(p, "entityKey");
                    if (string.IsNullOrWhiteSpace(entityKey))
                        return new ToolResult { IsSuccess = false, Error = "entityKey is required." };
                    var maxAssets = VisualToolParams.GetInt32(p, "maxAssets", 10);
                    var assets = await builder
                        .ListForEntityAsync(root, scenarioId, entityKey, maxAssets, CancellationToken.None)
                        .ConfigureAwait(false);
                    return new ToolResult
                    {
                        IsSuccess = true,
                        Output = VisualToolParams.ToJson(new { assets })
                    };
                }
                default:
                    return new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" };
            }
        }
        catch (Exception ex)
        {
            return new ToolResult { IsSuccess = false, Error = ex.Message };
        }
    }

    private static ToolResult OkContext(PersonVisualContextResult result) =>
        new()
        {
            IsSuccess = true,
            Output = VisualToolParams.ToJson(new
            {
                appendix = result.Appendix,
                assets = result.Assets,
                includeInLlm = true
            })
        };

    private static IReadOnlyList<string>? ParseEntityKeys(IDictionary<string, object> p)
    {
        var raw = VisualToolParams.GetString(p, "entityKeys");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static IReadOnlyList<string>? ParseAssetIds(IDictionary<string, object> p)
    {
        if (p.TryGetValue("assetIds", out var val) && val is IEnumerable<object> list)
        {
            return list
                .Select(x => Convert.ToString(x)?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
        }

        var raw = VisualToolParams.GetString(p, "assetIds");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
    }
}
