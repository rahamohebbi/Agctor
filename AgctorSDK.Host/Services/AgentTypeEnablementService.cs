using System.Text.Json;
using System.Text.Json.Nodes;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Reads/writes Agctor:AgentTypeEnablement in appsettings.User.json and applies runtime teardown when disabled.
/// </summary>
public sealed class AgentTypeEnablementService : IAgentTypeEnablementService
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<AgentTypeOptions> _agentTypeOptions;
    private readonly IHostEnvironment _environment;
    private readonly IAgentFactory _agentFactory;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ILogger<AgentTypeEnablementService> _logger;

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public AgentTypeEnablementService(
        IConfiguration configuration,
        IOptions<AgentTypeOptions> agentTypeOptions,
        IHostEnvironment environment,
        IAgentFactory agentFactory,
        IAgentRegistry agentRegistry,
        ILogger<AgentTypeEnablementService> logger)
    {
        _configuration = configuration;
        _agentTypeOptions = agentTypeOptions;
        _environment = environment;
        _agentFactory = agentFactory;
        _agentRegistry = agentRegistry;
        _logger = logger;
    }

    private string UserSettingsPath => Path.Combine(_environment.ContentRootPath, "appsettings.User.json");

    public IReadOnlyDictionary<string, bool> GetEffectiveEnablement(IReadOnlyDictionary<string, string> registeredAgentTypes)
    {
        var section = _configuration.GetSection("Agctor:AgentTypeEnablement");
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in registeredAgentTypes)
        {
            var v = section[kv.Key];
            if (string.IsNullOrEmpty(v) || !bool.TryParse(v, out var b))
                result[kv.Key] = true;
            else
                result[kv.Key] = b;
        }

        return result;
    }

    public bool IsTypeEnabled(string logicalTypeKey)
    {
        if (string.IsNullOrWhiteSpace(logicalTypeKey))
            return true;

        if (!_agentTypeOptions.Value.AgentTypes.ContainsKey(logicalTypeKey) &&
            !_agentTypeOptions.Value.AgentTypes.Keys.Any(k => string.Equals(k, logicalTypeKey, StringComparison.OrdinalIgnoreCase)))
        {
            // Types not registered in AgentTypeOptions (e.g. CodeGraph-only actors) stay enabled.
            return true;
        }

        var key = _agentTypeOptions.Value.AgentTypes.Keys.First(k => string.Equals(k, logicalTypeKey, StringComparison.OrdinalIgnoreCase));
        var raw = _configuration[$"Agctor:AgentTypeEnablement:{key}"];
        if (string.IsNullOrEmpty(raw) || !bool.TryParse(raw, out var b))
            return true;
        return b;
    }

    public async Task SetTypeEnabledAsync(string logicalTypeKey, bool enabled, CancellationToken cancellationToken = default)
    {
        ValidateLogicalKey(logicalTypeKey);
        await PersistEnablementAsync(logicalTypeKey, enabled, cancellationToken).ConfigureAwait(false);
        if (!enabled)
            await StopAgentsOfLogicalTypeAsync(logicalTypeKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAgentsOfLogicalTypeAsync(string logicalTypeKey, CancellationToken cancellationToken = default)
    {
        var agents = (await _agentRegistry.GetAllAgentsAsync().ConfigureAwait(false)).ToList();
        foreach (var agent in agents)
        {
            if (!string.Equals(agent.GetType().Name, logicalTypeKey, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                await _agentFactory.StopAgentAsync(agent.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StopAgentAsync failed for {AgentId}", agent.Id);
            }

            try
            {
                await _agentRegistry.UnregisterAgentAsync(agent.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unregister after stop for {AgentId}", agent.Id);
            }
        }
    }

    private void ValidateLogicalKey(string logicalTypeKey)
    {
        if (string.IsNullOrWhiteSpace(logicalTypeKey))
            throw new ArgumentException("Agent type name is required.", nameof(logicalTypeKey));
        if (!_agentTypeOptions.Value.AgentTypes.Keys.Any(k => string.Equals(k, logicalTypeKey, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unknown agent type '{logicalTypeKey}'.", nameof(logicalTypeKey));
    }

    private async Task PersistEnablementAsync(string logicalTypeKey, bool enabled, CancellationToken cancellationToken)
    {
        var canonicalKey = _agentTypeOptions.Value.AgentTypes.Keys.First(k =>
            string.Equals(k, logicalTypeKey, StringComparison.OrdinalIgnoreCase));

        JsonObject root;
        if (File.Exists(UserSettingsPath))
        {
            var text = await File.ReadAllTextAsync(UserSettingsPath, cancellationToken).ConfigureAwait(false);
            root = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        var agctor = root["Agctor"]?.AsObject() ?? new JsonObject();
        root["Agctor"] = agctor;

        var enablement = agctor["AgentTypeEnablement"]?.AsObject() ?? new JsonObject();
        agctor["AgentTypeEnablement"] = enablement;
        enablement[canonicalKey] = enabled;

        var json = root.ToJsonString(JsonWriteOptions);
        await File.WriteAllTextAsync(UserSettingsPath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated agent type enablement: {Type}={Enabled} in {Path}", canonicalKey, enabled, UserSettingsPath);
    }
}
