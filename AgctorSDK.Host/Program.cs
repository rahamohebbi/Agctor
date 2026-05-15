using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Agents;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Services.ProjectMemory;
using AgctorSDK.Host.Services.Scenarios;
using AgctorSDK.Host.Mcp;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Snippets;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Agents.ProjectMemory;
using AgctorSDK.Extensions.DependencyInjection;
using AgctorSDK.Extensions.Services;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Host.Services.Sessions;
using AgctorSDK.Host.Services.Traces;
using AgctorSDK.Core.Streaming;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// User-local overrides (agent type enablement, etc.) — PRD-010; file is gitignored.
var userSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.User.json");
builder.Configuration.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: true);

// Ensure a dedicated default HTTP URL so startup does not collide with macOS services on port 5000.
if (string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"]) &&
    string.IsNullOrWhiteSpace(builder.WebHost.GetSetting("urls")))
{
    builder.WebHost.UseUrls("http://localhost:5274");
}

// Add services to the container — camelCase JSON so browser fetch() matches JS (s.flow, personaAgentIds, …).
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "AGCTOR Host API", 
        Version = "v1",
        Description = "HTTP + MCP Integration Gateway for the AGCTOR Agent Framework"
    });
    
    // Enable XML documentation for better Swagger docs
    // Commented out for now as XML file might not exist
    /*
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
    */
});

// Configure agent types
builder.Services.Configure<AgentTypeOptions>(options =>
{
    options.RegisterAgentType("Agent", typeof(Agent));
    options.RegisterAgentType("LLMAgent", typeof(LLMAgent));
    options.RegisterAgentType("CoderAgent", typeof(CoderAgent));
    options.RegisterAgentType("SessionCoordinatorAgent", typeof(SessionCoordinatorAgent));
    options.RegisterAgentType("SessionMemoryAgent", typeof(SessionMemoryAgent));
    options.RegisterAgentType("PersonExtractorProjectAgent", typeof(PersonExtractorProjectAgent));
    options.RegisterAgentType("MemoryCuratorProjectAgent", typeof(MemoryCuratorProjectAgent));
    options.RegisterAgentType("PersonQueryProjectAgent", typeof(PersonQueryProjectAgent));
});

var defaultProjectMemoryRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "samples", "people-project"));
builder.Services.Configure<ProjectMemoryAgentOptions>(o =>
{
    var cfgPath = builder.Configuration["Agctor:ProjectMemory:ProjectRoot"];
    o.ProjectRoot = !string.IsNullOrWhiteSpace(cfgPath) ? Path.GetFullPath(cfgPath) : defaultProjectMemoryRoot;
    if (Enum.TryParse<ProjectMemoryPipelineExecutionMode>(
            builder.Configuration["Agctor:ProjectMemory:ExecutionMode"],
            ignoreCase: true,
            out var executionMode))
    {
        o.ExecutionMode = executionMode;
    }
});
builder.Services.AddAgctorProjectMemory();
// PRD-018: entity-resolution subsystem (signal producers, metrics, bootstrapper).
builder.Services.AddAgctorResolution();
builder.Services.AddSingleton<AgctorSDK.Core.ProjectMemory.Resolution.Trace.IResolveSpanSink>(sp =>
    new AgctorSDK.Host.Services.ProjectMemory.ResolveSpanTraceSink(sp.GetService<AgctorSDK.Core.Utils.ActivityTracking.IActivityTracker>()));
builder.Services.AddSingleton<IProjectMemoryLlmClient, OllamaConfiguredCompletionClient>();
// PRD-019 Host classifier: heuristic fast path + LLM fallback so natural consent phrasing
// (e.g. "yes I wish to save it") is recognized without code edits.
builder.Services.AddSingleton<AgctorSDK.Core.ProjectMemory.OutOfSchema.IConfirmationIntentClassifier,
    AgctorSDK.Core.ProjectMemory.OutOfSchema.LlmConfirmationIntentClassifier>();
// PRD-019 Option B: LLM-driven multilingual coreference resolver is wired automatically by
// AddAgctorProjectMemory whenever an IProjectMemoryLlmClient is registered (always true here),
// so no explicit Host registration is required.
builder.Services.AddSingleton<ProjectMemoryPipelineRunner>();
builder.Services.AddSingleton<IProjectMemoryPipelineRunner>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProjectMemoryAgentOptions>>().Value;
    return options.ExecutionMode == ProjectMemoryPipelineExecutionMode.ActorWorkflow
        ? new ActorBackedProjectMemoryPipelineRunner(
            sp.GetRequiredService<IActorRuntimeAdapter>(),
            sp.GetRequiredService<ProjectMemoryPipelineRunner>())
        : sp.GetRequiredService<ProjectMemoryPipelineRunner>();
});
builder.Services.AddSingleton<IProjectMemoryFileService, ProjectMemoryFileService>();
builder.Services.AddSingleton<IProjectMemoryAgentYamlPersistence, ProjectMemoryAgentYamlPersistence>();
builder.Services.AddSingleton<IUserProjectMemorySettingsService, UserProjectMemorySettingsService>();
builder.Services.AddSingleton<ILlmUserSettingsService, LlmUserSettingsService>();
builder.Services.AddSingleton<IOllamaModelCatalog, OllamaModelCatalog>();

// Register AGCTOR Core services
var defaultRuntime = builder.Configuration.GetValue<string>("Agctor:DefaultRuntime", "InMemory");
Console.WriteLine($"🔄 Configured actor runtime: {defaultRuntime}");
var llmApiUrl = builder.Configuration.GetValue<string>("Agctor:LLM:OllamaApiUrl", "http://localhost:11434");
var llmModel = builder.Configuration.GetValue<string>("Agctor:LLM:DefaultModel", "mistral");
var configuredMcpPort = builder.Configuration.GetValue<int?>("Mcp:Port") ?? 8080;
LLMAgent.ConfigureDefaults(llmApiUrl, llmModel);
Console.WriteLine($"🤖 Configured LLM defaults: apiUrl={LLMAgent.GetConfiguredOllamaApiUrl()}, model={LLMAgent.GetConfiguredDefaultModel()}");

switch (defaultRuntime)
{
    case "Proto":
    case "Proto.Actor":
        builder.Services.AddAgctor<AgctorSDK.Core.Adapters.ProtoActorAdapter>(opts => opts.DefaultRuntime = "Proto.Actor");
        break;
    case "Orleans":
        builder.Services.AddAgctor<AgctorSDK.Core.Adapters.OrleansAdapter>(opts => opts.DefaultRuntime = "Orleans");
        break;
    default:
        builder.Services.AddAgctor<AgctorSDK.Core.Adapters.InMemoryActorRuntime>(opts => opts.DefaultRuntime = "InMemory");
        break;
}

// Use the lightweight in-process tracker for host startup reliability.
builder.Services.AddAgctorActivityTracking(opts =>
{
    opts.EnableToolTracing = true;
});
builder.Services.AddAgctorVisualization();

// Register Host-specific services
builder.Services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
builder.Services.AddSingleton<IAgentOutputStreamRegistry, AgentOutputStreamRegistry>();
builder.Services.AddSingleton<IMessageDispatcher, MessageDispatcher>();
builder.Services.AddSingleton(_ => AgctorToolCatalog.CreateDefault());
builder.Services.AddSingleton<IToolAgentsInsightService, ToolAgentsInsightService>();
builder.Services.AddSingleton<IToolInvoker, ToolInvoker>();
var sessionStorePath = builder.Configuration.GetValue<string>("Agctor:SessionStorePath")
    ?? Path.Combine(AppContext.BaseDirectory, "data", $"agctor-sessions-{configuredMcpPort}.db");
builder.Services.AddSingleton(new SessionMemoryOptions
{
    RecentTurnWindow = builder.Configuration.GetValue<int?>("Agctor:SessionMemory:RecentTurnWindow") ?? 8,
    SummaryRefreshTurns = builder.Configuration.GetValue<int?>("Agctor:SessionMemory:SummaryRefreshTurns") ?? 12,
    MaxContextChars = builder.Configuration.GetValue<int?>("Agctor:SessionMemory:MaxContextChars") ?? 12000
});
builder.Services.AddSingleton<ISessionStore>(_ => new SqliteSessionStore(sessionStorePath));
var traceStorePath = builder.Configuration.GetValue<string>("Agctor:TraceStorePath")
    ?? Path.Combine(AppContext.BaseDirectory, "data", $"agctor-traces-{configuredMcpPort}.db");
builder.Services.AddSingleton<ITraceTimelineStore>(_ => new SqliteTraceTimelineStore(traceStorePath));
builder.Services.AddSingleton<ISessionContextComposer, SessionContextComposer>();
// Register InMemoryTaskStore
builder.Services.AddInMemoryTaskStore();
// Code generation + pull-request automation
builder.Services.AddPullRequestAutomation();
// Configure background-service options, but start them after HTTP startup so the dashboard remains reachable.
builder.Services.Configure<TaskScoperHostedService.TaskScoperOptions>(options =>
{
    var seconds = builder.Configuration.GetValue<int?>("TaskScoper:ScanInterval");
    if (seconds.HasValue && seconds.Value > 0)
    {
        options.ScanInterval = TimeSpan.FromSeconds(seconds.Value);
    }
});
builder.Services.Configure<TaskFlowHostedService.TaskFlowOptions>(options =>
{
    var seconds = builder.Configuration.GetValue<int?>("TaskFlow:Interval");
    if (seconds.HasValue && seconds.Value > 0)
    {
        options.Interval = TimeSpan.FromSeconds(seconds.Value);
    }
});
builder.Services.AddSingleton<TaskScoperHostedService>();
builder.Services.AddSingleton<TaskFlowHostedService>();
// Register InMemoryGoalStore
builder.Services.AddInMemoryGoalStore();

// Register LLM client (Ollama default)
builder.Services.AddHttpClient<OllamaLlmClient>();
builder.Services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OllamaLlmClient>());

// Register scenario services
builder.Services.Configure<ScenarioCatalogOptions>(builder.Configuration.GetSection("Agctor:Scenarios"));
builder.Services.AddSingleton<IScenarioCatalog, JsonScenarioCatalog>();
builder.Services.AddSingleton<IScenarioFactory, ScenarioFactory>();
builder.Services.AddSingleton<ICurrentScenarioStore, CurrentScenarioStore>();
builder.Services.AddSingleton<IScenarioApplicationService, ScenarioApplicationService>();
builder.Services.AddSingleton<IProjectMemoryPersonaLlmRunner, ProjectMemoryPersonaLlmRunner>();
builder.Services.AddSingleton<IScenarioFlowRouterLlmService, ScenarioFlowRouterLlmService>();
builder.Services.AddSingleton<IScenarioFlowExecutionService, ScenarioFlowExecutionService>();

// Agent type enablement persisted to appsettings.User.json (PRD-010)
builder.Services.AddSingleton<IAgentTypeEnablementService, AgentTypeEnablementService>();

// Dashboard config service (PRD-006)
builder.Services.AddSingleton<IHostConfigurationService, HostConfigurationService>();

// Actor runtime selection persisted to appsettings.User.json (PRD-012 Tier A)
builder.Services.AddSingleton<IUserRuntimeSettingsService, UserRuntimeSettingsService>();

// Agent detail providers for dashboard (PRD-006)
builder.Services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProvider, AgctorSDK.Host.Services.AgentDetailProviders.LLMAgentDetailProvider>();
builder.Services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProvider, AgctorSDK.Host.Services.AgentDetailProviders.CoderAgentDetailProvider>();
builder.Services.AddSingleton<AgctorSDK.Core.Interfaces.IAgentDetailProviderRegistry, AgentDetailProviderRegistry>();

// CodeGraph context for dashboard (PRD-006); scenario sets context when code-graph-demo runs
builder.Services.AddSingleton<ICodeGraphContextAccessor, CodeGraphContextAccessor>();

// Register MCP listener as a singleton and start it after HTTP is up.
builder.Services.AddSingleton<McpListener>();

// Register endpoint info so tests can discover chosen port when 0 is used
builder.Services.AddSingleton<AgctorSDK.Host.Models.McpEndpointInfo>();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

ProjectMemoryServiceAccessor.Initialize(app.Services);

// LLMAgent publishes via static hub (PRD-011).
AgentOutputStreamHub.Registry = app.Services.GetRequiredService<IAgentOutputStreamRegistry>();

// Register built-in snippet providers (C#, Python, etc.)
AgctorSDK.CodeGraph.Snippets.SnippetProviderBootstrapper.RegisterBuiltIn();

// Initialize the actor runtime before starting the application
Console.WriteLine("🔧 Initializing Actor Runtime...");
var runtime = app.Services.GetRequiredService<IActorRuntimeAdapter>();
var runtimeConfig = new Dictionary<string, object>
{
    ["Environment"] = app.Environment.EnvironmentName,
    ["MaxConcurrentMessages"] = 1000,
    ["DefaultTimeoutMs"] = 30000
};

if (runtime.Name == "Proto.Actor")
{
    runtimeConfig["remoteHost"] = builder.Configuration.GetValue<string>("Agctor:ProtoHost", "127.0.0.1") ?? "127.0.0.1";
    runtimeConfig["remotePort"] = builder.Configuration.GetValue("Agctor:ProtoPort", 12000);
}

await runtime.InitializeAsync(runtimeConfig);
Console.WriteLine("✅ Actor Runtime initialized successfully");

// PRD-018: bootstrap the entity-resolution subsystem for the configured project root (if any).
// Safe when the subsystem is disabled — the supervisor still spawns but does no work.
try
{
    var resolutionProjectRoot = Path.GetFullPath(
        builder.Configuration["Agctor:ProjectMemory:ProjectRoot"]?.Trim() ?? defaultProjectMemoryRoot);
    if (Directory.Exists(Path.Combine(resolutionProjectRoot, ".agctor")))
    {
        var bootstrap = app.Services.GetRequiredService<AgctorSDK.Core.ProjectMemory.Resolution.ResolutionBootstrapper>();
        var projectId = Path.GetFileName(Path.TrimEndingDirectorySeparator(resolutionProjectRoot));
        await bootstrap.StartAsync(resolutionProjectRoot, projectId);
        Console.WriteLine($"🔗 Resolution subsystem bootstrapped for project '{projectId}' at {resolutionProjectRoot}");
    }
}
catch (Exception resEx)
{
    Console.WriteLine($"⚠️  Resolution bootstrap skipped: {resEx.Message}");
}

// Keep session coordination available even before a demo scenario is applied.
var startupAgentFactory = app.Services.GetRequiredService<IAgentFactory>();
// Tools are IToolActor instances — registered from AgctorToolCatalog (same list as HTTP discovery + extra actors).
var toolCatalog = app.Services.GetRequiredService<AgctorToolCatalog>();
toolCatalog.RegisterToolActorTypes(startupAgentFactory);
var startupAgentRegistry = app.Services.GetRequiredService<IAgentRegistry>();
var startupEnablement = app.Services.GetRequiredService<IAgentTypeEnablementService>();
if (await startupAgentRegistry.GetAgentByIdAsync("session-coordinator-agent") == null
    && startupEnablement.IsTypeEnabled("SessionCoordinatorAgent"))
{
    var sessionStore = app.Services.GetRequiredService<ISessionStore>();
    var sessionComposer = app.Services.GetRequiredService<ISessionContextComposer>();
    var sessionOptions = app.Services.GetRequiredService<SessionMemoryOptions>();
    var startupSessionCoordinator = await runtime.SpawnActorAsync<SessionCoordinatorAgent>(
        "session-coordinator-agent",
        id => new SessionCoordinatorAgent(id, sessionStore, sessionComposer, sessionOptions));
    startupSessionCoordinator.SetAgentFactory(startupAgentFactory);
    await startupAgentRegistry.RegisterAgentAsync(startupSessionCoordinator);
}

// Spawn SnippetResolverAgent (LLM fallback for snippets)
var llmClient = app.Services.GetRequiredService<ILlmClient>();
var snippetResolver = await runtime.SpawnActorAsync("snippet-resolver", id => new SnippetResolverAgent(id, llmClient));
AgctorSDK.CodeGraph.Snippets.SnippetProviderRegistry.Register(snippetResolver);

// Configure the HTTP request pipeline
// Enable Swagger in all environments for API documentation
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AGCTOR Host API v1");
    c.RoutePrefix = "swagger";
});

if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}

app.UseAuthorization();
app.UseStaticFiles();
app.MapControllers();
app.MapRazorPages();
app.MapGet("/", () => Results.Redirect("/swagger/"));
app.MapGet("/swagger", () => Results.Redirect("/swagger/"));

// The MCP listener uses a separate TCP port from the HTTP server.
var configuredPort = builder.Configuration.GetValue<int>("Mcp:Port", configuredMcpPort);
Console.WriteLine($"🔌 MCP listener configured to start on TCP port {configuredPort} (0 means dynamic)");

// Advertise the expected HTTP URL before entering the host run loop.
var configuredHttpUrls = builder.Configuration["ASPNETCORE_URLS"]
    ?? builder.WebHost.GetSetting("urls")
    ?? "http://localhost:5274";
Console.WriteLine($"🌐 HTTP server starting on: {configuredHttpUrls}");

var primaryHttpUrl = configuredHttpUrls
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .FirstOrDefault(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
if (!string.IsNullOrWhiteSpace(primaryHttpUrl))
{
    Console.WriteLine($"📘 Swagger UI: {primaryHttpUrl.TrimEnd('/')}/swagger");
}

// Keep MCP startup independent from the HTTP host so Swagger remains reachable.
McpListener? mcpListener = null;
TaskScoperHostedService? taskScoper = null;
TaskFlowHostedService? taskFlow = null;
app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"🌐 HTTP server listening on: {string.Join(", ", app.Urls)}");

    mcpListener = app.Services.GetRequiredService<McpListener>();
    _ = Task.Run(async () =>
    {
        try
        {
            await mcpListener.StartAsync(app.Lifetime.ApplicationStopping);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ MCP listener failed to start: {ex.Message}");
        }
    });

    taskScoper = app.Services.GetRequiredService<TaskScoperHostedService>();
    _ = Task.Run(async () =>
    {
        try
        {
            await taskScoper.StartAsync(app.Lifetime.ApplicationStopping);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Task scoper failed to start: {ex.Message}");
        }
    });

    taskFlow = app.Services.GetRequiredService<TaskFlowHostedService>();
    _ = Task.Run(async () =>
    {
        try
        {
            await taskFlow.StartAsync(app.Lifetime.ApplicationStopping);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Task flow failed to start: {ex.Message}");
        }
    });
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    if (mcpListener != null)
    {
        _ = Task.Run(() => mcpListener.StopAsync(CancellationToken.None));
    }

    if (taskScoper != null)
    {
        _ = Task.Run(() => taskScoper.StopAsync(CancellationToken.None));
    }

    if (taskFlow != null)
    {
        _ = Task.Run(() => taskFlow.StopAsync(CancellationToken.None));
    }
});

app.Run();

// Make Program class accessible for integration testing
public partial class Program { } 