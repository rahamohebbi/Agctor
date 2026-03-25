namespace AgctorSDK.Host.Services;

/// <summary>
/// Per registered agent-type enablement persisted in appsettings.User.json (PRD-010).
/// Unknown CLR-only types are always treated as enabled for scenario spawning.
/// </summary>
public interface IAgentTypeEnablementService
{
    /// <summary>Merged map: each key from registered agent types, default true when unset.</summary>
    IReadOnlyDictionary<string, bool> GetEffectiveEnablement(IReadOnlyDictionary<string, string> registeredAgentTypes);

    /// <summary>Whether a logical type key (e.g. LLMAgent, Agent) is enabled.</summary>
    bool IsTypeEnabled(string logicalTypeKey);

    Task SetTypeEnabledAsync(string logicalTypeKey, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Stops and unregisters running agents whose CLR type name matches the logical key.</summary>
    Task StopAgentsOfLogicalTypeAsync(string logicalTypeKey, CancellationToken cancellationToken = default);
}
