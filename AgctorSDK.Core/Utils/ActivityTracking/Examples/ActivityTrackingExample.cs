using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.ActivityTracking.Logger;
using AgctorSDK.Core.Utils.ActivityTracking.OpenTelemetry;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgctorSDK.Core.Utils.ActivityTracking.Examples
{
    /// <summary>
    /// Example showing how to integrate activity tracking in a complete application.
    /// </summary>
    public static class ActivityTrackingExample
    {
        /// <summary>
        /// Run a complete example showing both logger-based and OpenTelemetry-based activity tracking.
        /// </summary>
        public static async Task RunCompleteExampleAsync(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            
            Console.WriteLine("Starting Activity Tracking Example...");
            
            // Run with Logger-based tracking
            Console.WriteLine("\n=== Logger-Based Activity Tracking ===");
            await RunExampleAsync(host, useOpenTelemetry: false);
            
            // Run with OpenTelemetry-based tracking
            Console.WriteLine("\n=== OpenTelemetry-Based Activity Tracking ===");
            await RunExampleAsync(host, useOpenTelemetry: true);
            
            Console.WriteLine("\nExample completed.");
        }
        
        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // Register core services
                    services.AddSingleton<IAgctorLogger, ConsoleLogger>();
                    
                    // Register both activity tracking implementations
                    // but don't enable them by default
                    services.AddSingleton<LoggerActivityTracker>();
                    services.AddSingleton<OpenTelemetryActivityTracker>();
                    
                    // Register your application services
                    services.AddSingleton<ExampleService>();
                });
        
        private static async Task RunExampleAsync(IHost host, bool useOpenTelemetry)
        {
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                
                // Configure the appropriate activity tracker
                IActivityTracker activityTracker;
                if (useOpenTelemetry)
                {
                    // In a real application, this would be configured through DI
                    // with AddAgctorOpenTelemetryTracking() during startup
                    activityTracker = services.GetRequiredService<OpenTelemetryActivityTracker>();
                    Console.WriteLine("Using OpenTelemetry-based activity tracking");
                }
                else
                {
                    // In a real application, this would be configured through DI
                    // with AddAgctorActivityTracking() during startup
                    activityTracker = services.GetRequiredService<LoggerActivityTracker>();
                    Console.WriteLine("Using Logger-based activity tracking");
                }
                
                // Run the example service with the selected activity tracker
                var exampleService = services.GetRequiredService<ExampleService>();
                await exampleService.ProcessWorkAsync(activityTracker);
            }
        }
    }
    
    /// <summary>
    /// Example service that uses activity tracking.
    /// </summary>
    public class ExampleService
    {
        private readonly IAgctorLogger _logger;
        
        public ExampleService(IAgctorLogger logger)
        {
            _logger = logger;
        }
        
        public async Task ProcessWorkAsync(IActivityTracker activityTracker)
        {
            // Start a main activity
            using (var mainActivity = activityTracker.StartActivity("Process Main Work"))
            {
                mainActivity.SetAttribute("service", "ExampleService");
                mainActivity.SetAttribute("operation", "ProcessWork");
                
                _logger.Info("Starting main processing work");
                
                try
                {
                    // Record the starting event
                    mainActivity.RecordEvent("Work Started", new Dictionary<string, object>
                    {
                        { "timestamp", DateTime.UtcNow }
                    });
                    
                    // Do some initial work
                    await Task.Delay(100);
                    
                    // Process multiple sub-items
                    for (int i = 0; i < 3; i++)
                    {
                        await ProcessSubItemAsync(i, activityTracker);
                    }
                    
                    // Record completion event
                    mainActivity.RecordEvent("Work Completed", new Dictionary<string, object>
                    {
                        { "timestamp", DateTime.UtcNow },
                        { "items_processed", 3 }
                    });
                    
                    mainActivity.SetStatus(ActivityStatus.Ok, "All items processed successfully");
                    _logger.Info("Main processing work completed");
                }
                catch (Exception ex)
                {
                    mainActivity.RecordException(ex);
                    mainActivity.SetStatus(ActivityStatus.Error, ex.Message);
                    _logger.Error(ex, "Error in main processing work");
                    throw;
                }
            }
        }
        
        private async Task ProcessSubItemAsync(int itemId, IActivityTracker activityTracker)
        {
            // Extract context from parent activity
            var parentContext = activityTracker.ExtractContext();
            
            // Convert IDictionary to IReadOnlyDictionary
            Dictionary<string, string> readonlyContext = new Dictionary<string, string>(parentContext);
            
            // Start a sub-activity with the parent context
            using (var activity = activityTracker.StartActivity($"Process Item {itemId}", readonlyContext))
            {
                activity.SetAttribute("item_id", itemId.ToString());
                
                _logger.Info($"Processing item {itemId}");
                
                // Simulate work
                await Task.Delay(200);
                
                // Simulate a potential error for the second item
                if (itemId == 1 && DateTime.UtcNow.Millisecond % 2 == 0)
                {
                    var warning = $"Warning condition detected for item {itemId}";
                    _logger.Warning(warning);
                    activity.RecordEvent("Warning", new Dictionary<string, object>
                    {
                        { "message", warning },
                        { "severity", "warning" }
                    });
                }
                
                activity.RecordEvent("Item Processed");
                activity.SetStatus(ActivityStatus.Ok);
                
                _logger.Info($"Completed processing item {itemId}");
            }
        }
    }
    
    /// <summary>
    /// Simple console logger implementation for the example.
    /// </summary>
    internal class ConsoleLogger : IAgctorLogger
    {
        public void Trace(string message, params object[] args) => Log("TRACE", FormatMessage(message, args));
        public void Debug(string message, params object[] args) => Log("DEBUG", FormatMessage(message, args));
        public void Info(string message, params object[] args) => Log("INFO", FormatMessage(message, args));
        public void Warning(string message, params object[] args) => Log("WARN", FormatMessage(message, args));
        public void Error(string message, params object[] args) => Log("ERROR", FormatMessage(message, args));
        public void Error(Exception ex, string message, params object[] args) => Log("ERROR", $"{FormatMessage(message, args)} - {ex}");
        public void Critical(string message, params object[] args) => Log("CRITICAL", FormatMessage(message, args));
        public void Critical(Exception ex, string message, params object[] args) => Log("CRITICAL", $"{FormatMessage(message, args)} - {ex}");
        public bool IsEnabled(LogLevel level) => true;
        
        private string FormatMessage(string message, object[] args)
        {
            if (args == null || args.Length == 0)
                return message;
            
            return string.Format(message, args);
        }
        
        private void Log(string level, string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {level}: {message}");
        }
    }
} 