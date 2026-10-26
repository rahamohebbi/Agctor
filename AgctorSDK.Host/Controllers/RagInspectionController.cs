using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory.Rag;
using AgctorSDK.Host.Services.Models;
using AgctorSDK.Host.Services.Rag;
using AgctorSDK.Host.Services.Scenarios;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgctorSDK.Host.Controllers;

[ApiController]
[Route("api/chat/sessions/{sessionId}/rag")]
public sealed class RagInspectionController(
    ISessionStore sessions,
    IRagInspectionStore inspections,
    ModelSelectionService models,
    IScenarioCatalog scenarios,
    RagMigrationService migrations) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(string sessionId, CancellationToken ct)
    {
        var session = await sessions.GetSessionAsync(sessionId, ct);
        if (session == null) return NotFound();

        var project = session.ProjectId == null ? null : await sessions.GetProjectAsync(session.ProjectId, ct);
        var scenarioId = project?.ScenarioId;
        var plan = await models.PlanAsync(sessionId, scenarioId, ct);
        var scenario = string.IsNullOrWhiteSpace(scenarioId) ? null : scenarios.Get(scenarioId);
        var contextSteps = (scenario?.Flow?.Nodes ?? [])
            .Where(n => string.Equals(n.Type, "LlmNode", StringComparison.OrdinalIgnoreCase))
            .Select(n => new
            {
                n.Id,
                n.Label,
                Strategy = ContextStrategy(n.Config)
            })
            .Where(n => n.Strategy != null)
            .ToArray();
        var latestTransfer = string.IsNullOrWhiteSpace(scenarioId)
            ? null
            : migrations.List(scenarioId).FirstOrDefault();
        var provider = plan.RagProvider;

        return Ok(new
        {
            scenarioId,
            provider = new { id = provider?.ProviderId ?? "None", source = provider?.Source ?? "global default" },
            retrievalSteps = contextSteps.Where(n => n.Strategy is "rag" or "graph_rag"),
            markdownSteps = contextSteps.Where(n => n.Strategy is "markdown_all" or "markdown_focus"),
            latestTransfer = latestTransfer == null ? null : new
            {
                latestTransfer.Id,
                latestTransfer.ProviderId,
                latestTransfer.Status,
                latestTransfer.Message,
                latestTransfer.CreatedAt,
                accepted = latestTransfer.Files.Count(f => f.Status == "accepted"),
                failed = latestTransfer.Files.Count(f => f.Status is "failed" or "uncertain"),
                total = latestTransfer.Files.Count
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(string sessionId, string? turnGroupId, string? traceId, CancellationToken ct)
    {
        if (await sessions.GetSessionAsync(sessionId, ct) == null) return NotFound();
        var records = await inspections.GetRagInspectionsAsync(sessionId, turnGroupId, traceId, ct);
        return Ok(records);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string sessionId, string id, CancellationToken ct)
    {
        var record = await inspections.GetRagInspectionAsync(sessionId, id, ct);
        return record == null ? NotFound() : Ok(record);
    }

    private static string? ContextStrategy(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("contextStrategy", out var strategy) ||
            strategy.ValueKind != JsonValueKind.String)
            return null;
        var result = strategy.GetString()?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
