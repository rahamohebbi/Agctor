using System.Text.Json;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.Rag;
using AgctorSDK.Host.Services.Models;
using AgctorSDK.Host.Services.Scenarios;
using Microsoft.AspNetCore.Mvc;

namespace AgctorSDK.Host.Controllers;

[ApiController]
[Route("api/model-settings")]
public sealed class ModelSettingsController(ISessionStore sessions, IScenarioCatalog scenarios, ModelSelectionService selection, ModelCapabilitiesService capabilities) : ControllerBase
{
    // Independent of Ollama availability: a RAG choice should still be editable offline.
    [HttpGet("rag-providers")]
    public IActionResult RagProviders([FromServices] IRagProviderAdapterFactory factory) =>
        Ok(new { providers = factory.GetAvailableProviders(), globalDefault = factory.GetDefaultProviderId() });

    [HttpGet("models")]
    public async Task<IActionResult> Models(CancellationToken ct)
    {
        try { return Ok(new { models = await capabilities.ListAsync(ct), applicationLlm = OllamaRuntimeConfiguration.GetDefaultModel() }); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return StatusCode(502, new { error = ex.Message }); }
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> Get(string sessionId, CancellationToken ct)
    {
        var session = await sessions.GetSessionAsync(sessionId, ct);
        if (session == null) return NotFound();
        var project = session.ProjectId == null ? null : await sessions.GetProjectAsync(session.ProjectId, ct);
        var scenarioId = project?.ScenarioId;
        var settings = await selection.SettingsAsync(sessionId, scenarioId, ct);
        var plan = await selection.PlanAsync(sessionId, scenarioId, ct);
        var flowNodes = scenarios.Get(scenarioId ?? "")?.Flow?.Nodes;
        return Ok(new { settings, plan,
            nodes = flowNodes?.Select(n => new { n.Id, n.Label, role = ModelSelectionService.Role(n) }).Where(n => n.role != null),
            ragNodes = flowNodes?.Where(n => ModelSelectionService.ConfigRagProvider(n) != null)
                .Select(n => new { n.Id, n.Label, providerId = RagProviderIds.Normalize(ModelSelectionService.ConfigRagProvider(n)) }) });
    }

    [HttpPut("sessions/{sessionId}")]
    public async Task<IActionResult> Save(string sessionId, [FromBody] SessionModelSettings settings, CancellationToken ct)
    {
        var session = await sessions.GetSessionAsync(sessionId, ct);
        if (session == null) return NotFound();
        var project = session.ProjectId == null ? null : await sessions.GetProjectAsync(session.ProjectId, ct);
        if (!string.Equals(settings.ScenarioId, project?.ScenarioId, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "The session's scenario changed. Reload model settings before saving." });
        settings.Defaults ??= new();
        if (!string.IsNullOrWhiteSpace(settings.RagProviderId) && !RagProviderIds.IsKnown(settings.RagProviderId))
            return BadRequest(new { error = $"Unknown RAG provider '{settings.RagProviderId}'." });
        settings.RagProviderId = string.IsNullOrWhiteSpace(settings.RagProviderId) ? null : RagProviderIds.Normalize(settings.RagProviderId);
        settings.Nodes ??= new();
        if (settings.Nodes.Count > 500) return BadRequest(new { error = "Too many node overrides." });
        var scenario = scenarios.Get(settings.ScenarioId ?? "");
        var checks = new List<ResolvedModel>();
        foreach (var role in ModelRoles.All)
            if (!string.IsNullOrWhiteSpace(settings.Defaults.ForRole(role))) checks.Add(new(role, settings.Defaults.ForRole(role)!.Trim(), "chat default"));
        foreach (var (id, nodeOverride) in settings.Nodes)
        {
            var node = scenario?.Flow?.Nodes.FirstOrDefault(n => n.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (node == null || nodeOverride == null || nodeOverride.Role != ModelSelectionService.Role(node) || string.IsNullOrWhiteSpace(nodeOverride.Model))
                return BadRequest(new { error = $"Invalid override for node '{id}'. Reload model settings." });
            checks.Add(new(nodeOverride.Role, nodeOverride.Model.Trim(), "chat node override"));
        }
        try { await capabilities.ValidateAsync(checks, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return BadRequest(new { error = ex.Message }); }
        await sessions.SaveModelSettingsJsonAsync(sessionId, JsonSerializer.Serialize(settings, ModelSelectionService.Json), ct);
        return await Get(sessionId, ct);
    }
}
