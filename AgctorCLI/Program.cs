using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Agents;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Registry;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgctorCLI;

/// <summary>CLI runner — same SDK stack as Host (Core, Agents, Tools, CodeGraph) without ASP.NET.</summary>
class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            ShowUsage();
            return;
        }

        var prompt = args[0];
        var runtimeName = args.Length > 1
            ? AgctorRuntimeCatalog.NormalizeRuntimeName(args[1])
            : AgctorRuntimeCatalog.InMemory;

        Console.WriteLine("🤖 Agctor CLI Agent Runner");
        Console.WriteLine($"📝 Prompt: {prompt}");
        Console.WriteLine($"⚙️  Runtime: {runtimeName}");
        Console.WriteLine();

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "AGCTOR_")
                .Build();

            var services = new ServiceCollection();
            services.AddLogging(b =>
            {
                b.AddConsole();
                b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
            });
            services.AddAgctorCli(configuration, runtimeName);

            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var runtime = await InitializeRuntimeAsync(serviceProvider, runtimeName, logger).ConfigureAwait(false);
            await CodeGraphCliBootstrap.InitializeAsync(serviceProvider, runtime).ConfigureAwait(false);

            var result = await ProcessPromptWithRootAgent(runtime, prompt, logger).ConfigureAwait(false);
            Console.WriteLine("✅ Result:");
            Console.WriteLine(result);

            await runtime.ShutdownAsync().ConfigureAwait(false);
            runtime.Dispose();
            await serviceProvider.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task<IActorRuntimeAdapter> InitializeRuntimeAsync(
        ServiceProvider serviceProvider,
        string runtimeName,
        ILogger logger)
    {
        logger.LogInformation("🚀 Initializing {RuntimeName} runtime...", runtimeName);

        var adapterFactory = serviceProvider.GetRequiredService<IActorRuntimeAdapterFactory>();
        if (!adapterFactory.IsRuntimeAvailable(runtimeName))
        {
            var available = string.Join(", ", adapterFactory.GetAvailableRuntimes());
            throw new InvalidOperationException(
                $"Runtime '{runtimeName}' is not available. Available: {available}. " +
                "Experimental runtimes require Agctor:AllowExperimentalRuntimes=true.");
        }

        var runtime = adapterFactory.CreateRuntime(runtimeName);
        await runtime.InitializeAsync(new Dictionary<string, object>
        {
            ["Environment"] = "CLI",
            ["MaxConcurrentMessages"] = 50,
            ["EnableMetrics"] = false
        }).ConfigureAwait(false);

        logger.LogInformation("✅ Runtime initialized successfully");
        return runtime;
    }

    private static async Task<string> ProcessPromptWithRootAgent(
        IActorRuntimeAdapter runtime,
        string prompt,
        ILogger logger)
    {
        logger.LogInformation("🤖 Creating root agent for prompt processing...");

        var services = new ServiceCollection();
        services.AddSingleton(runtime);
        var serviceProvider = services.BuildServiceProvider();

        var fileLogger = new FileLogger("cli-agent", new FileLoggerOptions(), AgctorSDK.Core.Utils.Logging.LogLevel.Info);
        var agentRegistry = new InMemoryAgentRegistry();
        var agentLogger = new AgctorFileLogger(fileLogger);
        var agentFactory = new AgentFactory(runtime, serviceProvider, agentLogger, agentRegistry);

        var toolCatalog = AgctorSDK.Extensions.Services.AgctorToolCatalog.CreateDefault();
        toolCatalog.RegisterToolActorTypes(agentFactory);

        var rootAgent = await agentFactory.SpawnAgentAsync<Agent>(
            prompt,
            agentId: $"cli-root-{Guid.NewGuid():N}").ConfigureAwait(false);

        logger.LogInformation("✅ Root agent created: {AgentId}", rootAgent.Id);
        logger.LogInformation("⏳ Processing prompt...");

        var maxWaitTime = TimeSpan.FromMinutes(5);
        var startTime = DateTime.UtcNow;

        while (rootAgent.Status != AgentStatus.Completed &&
               rootAgent.Status != AgentStatus.Failed &&
               DateTime.UtcNow - startTime < maxWaitTime)
        {
            await Task.Delay(500).ConfigureAwait(false);
            if ((DateTime.UtcNow - startTime).TotalSeconds % 10 == 0)
            {
                logger.LogInformation(
                    "⏳ Still processing... Status: {Status}, Children: {ChildCount}",
                    rootAgent.Status,
                    rootAgent.ChildAgentIds.Count);
            }
        }

        if (rootAgent.Status == AgentStatus.Completed)
        {
            logger.LogInformation("✅ Prompt processing completed successfully");
            return $"Prompt processed successfully by agent {rootAgent.Id}. " +
                   $"Agent spawned {rootAgent.ChildAgentIds.Count} child agents for subtask processing.";
        }

        if (rootAgent.Status == AgentStatus.Failed)
        {
            logger.LogError("❌ Prompt processing failed");
            return "Prompt processing failed. Please check the logs for more details.";
        }

        logger.LogWarning("⏰ Prompt processing timed out after {Timeout} minutes", maxWaitTime.TotalMinutes);
        return $"Prompt processing timed out. Agent status: {rootAgent.Status}";
    }

    private static void ShowUsage()
    {
        Console.WriteLine("🤖 Agctor CLI Agent Runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  AgctorCLI.exe \"Your prompt here\" [runtime]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  prompt   - The prompt or task to process (required)");
        Console.WriteLine("  runtime  - InMemory (default), Proto.Actor, or Orleans");
        Console.WriteLine();
        Console.WriteLine("Runtime notes:");
        Console.WriteLine("  InMemory is production-ready.");
        Console.WriteLine("  Proto.Actor and Orleans are experimental — set Agctor:AllowExperimentalRuntimes=true or AGCTOR_Agctor__AllowExperimentalRuntimes=true.");
    }
}
