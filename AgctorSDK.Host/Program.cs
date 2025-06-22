using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Mcp;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Snippets;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
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
    options.RegisterAgentType("CodeExecutorTool", typeof(CodeExecutorTool));
    options.RegisterAgentType("CompileTool", typeof(CompileTool));
});

// Register AGCTOR Core services
var defaultRuntime = builder.Configuration.GetValue<string>("Agctor:DefaultRuntime", "InMemory");
Console.WriteLine($"🔄 Configured actor runtime: {defaultRuntime}");

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

// Register Host-specific services
builder.Services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
builder.Services.AddSingleton<IMessageDispatcher, MessageDispatcher>();
builder.Services.AddSingleton<IToolInvoker, ToolInvoker>();

// Register LLM client (Ollama default)
builder.Services.AddHttpClient<OllamaLlmClient>();
builder.Services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OllamaLlmClient>());

// Register scenario services
builder.Services.AddSingleton<IScenarioFactory, ScenarioFactory>();

// Register MCP services as hosted services
builder.Services.AddHostedService<McpListener>();

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
    runtimeConfig["remoteHost"] = builder.Configuration.GetValue<string>("Agctor:ProtoHost", "127.0.0.1");
    runtimeConfig["remotePort"] = builder.Configuration.GetValue("Agctor:ProtoPort", 12000);
}

await runtime.InitializeAsync(runtimeConfig);
Console.WriteLine("✅ Actor Runtime initialized successfully");

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

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// The actual port may still be 0 here (ephemeral). Log configuration value only.
var configuredPort = builder.Configuration.GetValue<int>("Mcp:Port", 8080);
Console.WriteLine($"🔌 MCP listener configured to start on TCP port {configuredPort} (0 means dynamic)");

app.Run();

// Make Program class accessible for integration testing
public partial class Program { } 