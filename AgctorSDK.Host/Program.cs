using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Host.Services;
using AgctorSDK.Host.Mcp;

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

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

// Configure agent types
builder.Services.Configure<AgentTypeOptions>(options =>
{
    options.RegisterAgentType("Agent", typeof(Agent));
    options.RegisterAgentType("LLMAgent", typeof(LLMAgent));
    options.RegisterAgentType("CodeExecutorTool", typeof(CodeExecutorTool));
});

// Register AGCTOR Core services
builder.Services.AddAgctor();

// Register Host-specific services
builder.Services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
builder.Services.AddSingleton<IMessageDispatcher, MessageDispatcher>();
builder.Services.AddSingleton<IToolInvoker, ToolInvoker>();

// Register scenario services
builder.Services.AddSingleton<IScenarioFactory, ScenarioFactory>();

// Register MCP services as hosted services
builder.Services.AddHostedService<McpListener>();

// Register endpoint info so tests can discover chosen port when 0 is used
builder.Services.AddSingleton<AgctorSDK.Host.Models.McpEndpointInfo>();

// Wide-open CORS is development-only; do not enable AllowAll in production.
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

// Initialize the actor runtime before starting the application
Console.WriteLine("🔧 Initializing Actor Runtime...");
var runtime = app.Services.GetRequiredService<IActorRuntimeAdapter>();
await runtime.InitializeAsync(new Dictionary<string, object>
{
    ["Environment"] = app.Environment.EnvironmentName,
    ["MaxConcurrentMessages"] = 1000,
    ["DefaultTimeoutMs"] = 30000
});
Console.WriteLine("✅ Actor Runtime initialized successfully");

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