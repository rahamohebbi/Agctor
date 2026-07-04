using Orleans.Configuration;
using Orleans.Hosting;

var clusterId = Environment.GetEnvironmentVariable("ORLEANS_CLUSTER_ID") ?? "agctor-dev";
var serviceId = Environment.GetEnvironmentVariable("ORLEANS_SERVICE_ID") ?? "agctor-host";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = clusterId;
            options.ServiceId = serviceId;
        })
        .ConfigureEndpoints(siloPort: 11111, gatewayPort: 30000, listenOnAnyHostAddress: true)
        .UseLocalhostClustering();
});

builder.Services.AddHealthChecks();
var app = builder.Build();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new
{
    service = "agctor-orleans-silo",
    clusterId,
    serviceId,
    gatewayPort = 30000
}));

Console.WriteLine($"Orleans silo starting: cluster={clusterId}, service={serviceId}, gateway=30000");
await app.RunAsync();
