using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.Utils.ActivityTracking
{
    /// <summary>
    /// Demonstrates the use of the activity tracking system with both logger-based
    /// and OpenTelemetry-based implementations.
    /// </summary>
    public static class ActivityTrackingDemo
    {
        /// <summary>
        /// Runs a demo of the activity tracking system.
        /// </summary>
        public static async Task RunActivityTrackingDemoAsync()
        {
            Console.WriteLine("=== Activity Tracking Demo ===\n");
            
            // Use logging-based activity tracking
            Console.WriteLine("🔍 Testing Logger-Based Activity Tracking:");
            await RunDemoWithTracking(ConfigureLoggerBasedTracking);
            
            // Use OpenTelemetry-based activity tracking
            Console.WriteLine("\n📊 Testing OpenTelemetry-Based Activity Tracking:");
            await RunDemoWithTracking(ConfigureOpenTelemetryTracking);
        }
        
        private static void ConfigureLoggerBasedTracking(IServiceCollection services)
        {
            // Configure the logger-based activity tracking
            services.AddAgctorActivityTracking();
        }
        
        private static void ConfigureOpenTelemetryTracking(IServiceCollection services)
        {
            // Configure OpenTelemetry-based activity tracking
            services.AddAgctorOpenTelemetryTracking(options =>
            {
                options.SourceName = "Agctor.Demo";
                options.EnableZipkinExporter = true;
                options.ZipkinEndpoint = "http://localhost:9411/api/v2/spans";
                options.EnableOtlpExporter = false;
            });
        }

        private static async Task RunDemoWithTracking(Action<IServiceCollection> configureTracking)
        {
            // Create a service collection
            var services = new ServiceCollection();
            
            // Add a console logger
            services.AddSingleton<IAgctorLogger, ConsoleLogger>();
            
            // Configure activity tracking
            configureTracking(services);
            
            // Build the service provider
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the activity tracker
            var activityTracker = serviceProvider.GetRequiredService<IActivityTracker>();
            
            // Run the demo
            using (var mainActivity = activityTracker.StartActivity("Main Demo Activity"))
            {
                mainActivity.SetAttribute("demo", "activity-tracking");
                
                // Simulate a sub-activity
                await SimulateWorkAsync(activityTracker, "Process Data", mainActivity);
                
                // Simulate an error in a sub-activity
                try
                {
                    await SimulateErrorAsync(activityTracker, "Process Error", mainActivity);
                }
                catch (Exception ex)
                {
                    mainActivity.RecordException(ex);
                    Console.WriteLine($"Caught error: {ex.Message}");
                }
                
                // Record a final event
                mainActivity.RecordEvent("Demo Completed", new Dictionary<string, object> 
                {
                    { "success", true },
                    { "timestamp", DateTime.UtcNow }
                });
                
                mainActivity.SetStatus(ActivityStatus.Ok, "Demo completed successfully");
            }
        }
        
        private static async Task SimulateWorkAsync(
            IActivityTracker activityTracker, 
            string activityName,
            IActivityScope parentActivity)
        {
            // Extract context from parent activity
            var parentContext = activityTracker.ExtractContext();
            
            // Convert IDictionary to IReadOnlyDictionary
            Dictionary<string, string> readonlyContext = new Dictionary<string, string>(parentContext);
            
            // Start a new activity with the parent context
            using (var activity = activityTracker.StartActivity(activityName, readonlyContext))
            {
                activity.SetAttribute("operation", "data-processing");
                activity.RecordEvent("Starting work");
                
                // Simulate work
                await Task.Delay(500);
                
                activity.RecordEvent("Work complete");
                activity.SetStatus(ActivityStatus.Ok);
            }
        }
        
        private static async Task SimulateErrorAsync(
            IActivityTracker activityTracker,
            string activityName,
            IActivityScope parentActivity)
        {
            // Extract context from parent activity
            var parentContext = activityTracker.ExtractContext();
            
            // Convert IDictionary to IReadOnlyDictionary
            Dictionary<string, string> readonlyContext = new Dictionary<string, string>(parentContext);
            
            // Start a new activity with the parent context
            using (var activity = activityTracker.StartActivity(activityName, readonlyContext))
            {
                activity.SetAttribute("operation", "error-simulation");
                activity.RecordEvent("Starting risky operation");
                
                // Simulate work that results in an error
                await Task.Delay(300);
                
                // Simulate an error
                var exception = new InvalidOperationException("Simulated error occurred");
                activity.RecordException(exception);
                activity.SetStatus(ActivityStatus.Error, "Operation failed due to an error");
                
                // Rethrow the exception
                throw exception;
            }
        }
    }
    
    /// <summary>
    /// Simple console logger implementation for demo purposes.
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