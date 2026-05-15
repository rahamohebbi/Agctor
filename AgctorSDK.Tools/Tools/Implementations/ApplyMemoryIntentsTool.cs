using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Agents.ProjectMemory;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.Tools.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.Tools.Implementations;

/// <summary>
/// Writes curated memory: applies <see cref="Models.MemoryIntentBatch"/> JSON to markdown under scenario rules
/// (same steps as <c>MemoryCuratorProjectAgent</c>). Use only when policy allows this tool id.
/// </summary>
public sealed class ApplyMemoryIntentsTool : ToolActorBase
{
    public ApplyMemoryIntentsTool(string id) : base(id, nameof(ApplyMemoryIntentsTool))
    {
    }

    protected override Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(new ToolResult
        {
            IsSuccess = false,
            Error = "ApplyMemoryIntentsTool expects a ToolRequest with Operation Apply and batchJson parameter."
        });

    public override async Task<ToolResult> Handle(ToolRequest request)
    {
        if (!string.Equals(request.Operation, "Apply", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" };
        }

        var p = request.Parameters ?? new Dictionary<string, object>();
        var batchJson = GetString(p, "batchJson");
        if (string.IsNullOrWhiteSpace(batchJson))
            return new ToolResult { IsSuccess = false, Error = "Missing batchJson (MemoryIntentBatch JSON string)." };

        try
        {
            _ = ProjectMemoryServiceAccessor.GetRequiredService<IOptions<ProjectMemoryAgentOptions>>();
        }
        catch (InvalidOperationException ex)
        {
            return new ToolResult
            {
                IsSuccess = false,
                Error = "Project memory services are not initialized: " + ex.Message
            };
        }

        try
        {
            var services = ProjectMemoryAgentServices.Default;
            var json = await ProjectMemoryIntentApplier.ApplyFromJsonAsync(batchJson, services, CancellationToken.None)
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            {
                return new ToolResult { IsSuccess = false, Output = json, Error = err.GetString() ?? json };
            }

            if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
            {
                return new ToolResult { IsSuccess = false, Output = json, Error = "Memory intent routing reported errors." };
            }

            return new ToolResult { IsSuccess = true, Output = json };
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
