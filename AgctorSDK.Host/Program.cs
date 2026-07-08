using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Agents;
using AgctorSDK.Host.DependencyInjection;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Mcp;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Snippets;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Extensions.DependencyInjection;
using AgctorSDK.Extensions.Hosting;
using AgctorSDK.Extensions.Services;
using AgctorSDK.Core.Sessions;
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

var defaultProjectMemoryRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "samples", "people-project"));
var llmApiUrl = builder.Configuration.GetValue<string>("Agctor:LLM:OllamaApiUrl", "http://localhost:11434");
var llmModel = builder.Configuration.GetValue<string>("Agctor:LLM:DefaultModel", "mistral");
var visionModel = builder.Configuration.GetValue<string>("Agctor:LLM:VisionModel");
var visionFallbacks = builder.Configuration.GetSection("Agctor:LLM:VisionFallbackModels").Get<string[]>() ?? Array.Empty<string>();
var visionTimeout = builder.Configuration.GetValue<int?>("Agctor:LLM:VisionTimeoutSeconds");
var configuredMcpPort = builder.Configuration.GetValue<int?>("Mcp:Port") ?? 8080;
LLMAgent.ConfigureDefaults(llmApiUrl, llmModel);
OllamaRuntimeConfiguration.ConfigureVision(visionModel ?? llmModel, visionFallbacks, visionTimeout);
Console.WriteLine($"🤖 Configured LLM defaults: apiUrl={LLMAgent.GetConfiguredOllamaApiUrl()}, model={LLMAgent.GetConfiguredDefaultModel()}, vision={OllamaRuntimeConfiguration.GetVisionModel()}");

builder.Services.AddAgctorHost(builder.Configuration, defaultProjectMemoryRoot);
builder.Services.AddAgctorHostWeb(builder.Configuration, configuredMcpPort);

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
var runtimeSwitch = app.Services.GetRequiredService<IActorRuntimeSwitchService>();
await runtimeSwitch.InitializeFromConfigurationAsync();
var runtime = app.Services.GetRequiredService<IActorRuntimeAdapter>();
if (!runtime.IsInitialized)
    throw new InvalidOperationException("Actor runtime failed to initialize.");
Console.WriteLine($"✅ Actor Runtime initialized: {RuntimeCanonicalId.FromAdapter(runtime)}");

try
{
    var ollamaCatalog = app.Services.GetRequiredService<IOllamaModelCatalog>();
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OllamaVision");
    await OllamaVisionStartupProbe.LogVisionModelAvailabilityAsync(ollamaCatalog, startupLog);
}
catch (Exception exVisionProbe)
{
    Console.WriteLine($"⚠️ Vision model probe skipped: {exVisionProbe.Message}");
}

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