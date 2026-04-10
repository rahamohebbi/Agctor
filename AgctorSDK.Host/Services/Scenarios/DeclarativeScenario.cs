using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Generic setup for declarative scenarios loaded from JSON.
/// </summary>
public sealed class DeclarativeScenario : IScenario
{
    private readonly ScenarioDefinition _definition;
    private readonly IActorRuntimeAdapter _runtimeAdapter;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ISessionStore _sessionStore;
    private readonly ISessionContextComposer _sessionContextComposer;
    private readonly SessionMemoryOptions _sessionOptions;
    private readonly IAgentTypeEnablementService _enablement;
    private readonly ILogger<DeclarativeScenario> _logger;

    public DeclarativeScenario(
        ScenarioDefinition definition,
        IActorRuntimeAdapter runtimeAdapter,
        IAgentRegistry agentRegistry,
        ISessionStore sessionStore,
        ISessionContextComposer sessionContextComposer,
        SessionMemoryOptions sessionOptions,
        IAgentTypeEnablementService enablement,
        ILogger<DeclarativeScenario> logger)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _runtimeAdapter = runtimeAdapter;
        _agentRegistry = agentRegistry;
        _sessionStore = sessionStore;
        _sessionContextComposer = sessionContextComposer;
        _sessionOptions = sessionOptions;
        _enablement = enablement;
        _logger = logger;
    }

    public string Name => _definition.Id;
    public string Description => string.IsNullOrWhiteSpace(_definition.Description)
        ? $"Declarative scenario '{_definition.Id}'."
        : _definition.Description;

    public async Task<ScenarioSetupResponse> SetupAsync(Dictionary<string, object>? parameters = null)
    {
        var created = new List<string>();
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var agentType in _definition.AgentTypes)
        {
            try
            {
                var normalized = (agentType ?? string.Empty).Trim();
                if (normalized.Length == 0) continue;

                // V1 supports session bootstrap agents; unknown ids are reported but non-fatal.
                if (string.Equals(normalized, "SessionCoordinatorAgent", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_enablement.IsTypeEnabled("SessionCoordinatorAgent")) continue;
                    if (await _agentRegistry.GetAgentByIdAsync("session-coordinator-agent").ConfigureAwait(false) == null)
                    {
                        var a = await _runtimeAdapter.SpawnActorAsync<SessionCoordinatorAgent>(
                            "session-coordinator-agent",
                            id => new SessionCoordinatorAgent(id, _sessionStore, _sessionContextComposer, _sessionOptions)).ConfigureAwait(false);
                        await _agentRegistry.RegisterAgentAsync(a).ConfigureAwait(false);
                    }
                    created.Add("session-coordinator-agent");
                    roles["session-coordinator-agent"] = "Session routing and memory orchestration.";
                    continue;
                }

                if (string.Equals(normalized, "SessionMemoryAgent", StringComparison.OrdinalIgnoreCase))
                {
                    // Session memory actors are per-session and created by coordinator when needed.
                    roles["session-memory-agent"] = "Per-session memory actor (spawned on-demand).";
                    continue;
                }

                roles[normalized] = "Configured in declarative scenario (no built-in bootstrap for this id yet).";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Declarative scenario agent bootstrap failed for {AgentType}", agentType);
            }
        }

        return new ScenarioSetupResponse(
            Success: true,
            ScenarioName: Name,
            CreatedAgentIds: created.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            AgentRoles: roles,
            ErrorMessage: null);
    }
}

