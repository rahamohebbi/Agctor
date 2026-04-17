using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Agents;
using AgctorSDK.Host.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Aggregates Host configuration from config and services for the dashboard (PRD-006).
/// </summary>
public class HostConfigurationService : IHostConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<AgentTypeOptions> _agentTypeOptions;
    private readonly IToolInvoker _toolInvoker;
    private readonly IScenarioFactory _scenarioFactory;
    private readonly IActorRuntimeAdapter? _runtimeAdapter;
    private readonly IAgentTypeEnablementService _agentTypeEnablement;

    public HostConfigurationService(
        IConfiguration configuration,
        IOptions<AgentTypeOptions> agentTypeOptions,
        IToolInvoker toolInvoker,
        IScenarioFactory scenarioFactory,
        IAgentTypeEnablementService agentTypeEnablement,
        IActorRuntimeAdapter? runtimeAdapter = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _agentTypeOptions = agentTypeOptions ?? throw new ArgumentNullException(nameof(agentTypeOptions));
        _toolInvoker = toolInvoker ?? throw new ArgumentNullException(nameof(toolInvoker));
        _scenarioFactory = scenarioFactory ?? throw new ArgumentNullException(nameof(scenarioFactory));
        _agentTypeEnablement = agentTypeEnablement ?? throw new ArgumentNullException(nameof(agentTypeEnablement));
        _runtimeAdapter = runtimeAdapter;
    }

    public async Task<HostConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var runtimeName = _configuration.GetValue<string>("Agctor:DefaultRuntime", "InMemory");
        if (_runtimeAdapter != null)
            runtimeName = _runtimeAdapter.Name;

        var runtime = new RuntimeConfigDto
        {
            Name = runtimeName,
            ProtoHost = _configuration.GetValue<string>("Agctor:ProtoHost"),
            ProtoPort = _configuration.GetValue<int?>("Agctor:ProtoPort")
        };

        // Effective values from LLMAgent so /api/Config matches runtime after PUT /api/Llm/default-model (PRD-015).
        var llm = new LlmConfigDto
        {
            OllamaApiUrl = LLMAgent.GetConfiguredOllamaApiUrl(),
            DefaultModel = LLMAgent.GetConfiguredDefaultModel()
        };

        var mcp = new McpConfigDto
        {
            Host = _configuration.GetValue<string>("Mcp:Host", "0.0.0.0"),
            Port = _configuration.GetValue<int>("Mcp:Port", 8080)
        };

        var generatedCodeRoot = _configuration.GetValue<string>("Agctor:GeneratedCodeRoot")
            ?? Path.Combine(Path.GetTempPath(), "agctor-generated-code");

        var backgroundServices = new BackgroundServicesDto
        {
            TaskScoperScanIntervalSeconds = _configuration.GetValue("TaskScoper:ScanInterval", 30),
            TaskFlowIntervalSeconds = _configuration.GetValue("TaskFlow:Interval", 10)
        };

        var agentTypes = _agentTypeOptions.Value.AgentTypes
            .ToDictionary(k => k.Key, v => v.Value.FullName ?? v.Value.Name, StringComparer.OrdinalIgnoreCase);

        var enablement = _agentTypeEnablement.GetEffectiveEnablement(agentTypes);
        var dashboardScenario = _configuration.GetValue<string>("Agctor:Dashboard:ScenarioName") ?? "code-graph-demo";

        var toolIds = await _toolInvoker.GetAvailableToolsAsync(cancellationToken);
        var tools = new List<ToolInfo>();
        foreach (var id in toolIds)
        {
            var info = await _toolInvoker.GetToolInfoAsync(id, cancellationToken);
            if (info != null)
                tools.Add(info);
        }

        var scenarioDescriptions = _scenarioFactory.GetScenarioDescriptions();

        return new HostConfigurationDto
        {
            Runtime = runtime,
            Llm = llm,
            Mcp = mcp,
            GeneratedCodeRoot = generatedCodeRoot,
            BackgroundServices = backgroundServices,
            AgentTypes = agentTypes,
            AgentTypeEnablement = enablement,
            DashboardScenarioName = dashboardScenario,
            Tools = tools,
            Scenarios = scenarioDescriptions
        };
    }
}
