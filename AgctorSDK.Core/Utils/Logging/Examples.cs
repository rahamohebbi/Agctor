using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.Utils.Logging.Examples
{
    /// <summary>
    /// Examples demonstrating how to use the FileLogger with dependency injection.
    /// This class is for documentation purposes only and is not used directly in the code.
    /// </summary>
    public static class LoggingExamples
    {
        /// <summary>
        /// Example showing how to configure logging with DI in a console application.
        /// </summary>
        public static void ConfigureLoggingWithDI()
        {
            #if false // Examples are for documentation only, disabled at compile time
            // Create service collection
            var services = new ServiceCollection();
            
            // Example 1: Register a single file logger
            services.AddSingleton<IAgctorLogger>(sp => 
            {
                var options = new FileLoggerOptions
                {
                    LogDirectory = "logs",
                    FileName = "application-{date}.log",
                    RotationStrategy = RotationStrategy.Daily,
                    MaxDaysToKeep = 30
                };
                
                return new FileLogger("Application", options, LogLevel.Info);
            });
            
            // Example 2: Register multiple loggers through a provider factory
            services.AddSingleton<ILoggerProvider>(sp =>
            {
                return new FileLoggerProvider(new FileLoggerOptions
                {
                    LogDirectory = "logs/errors",
                    FileName = "error-{date}.log",
                    RotationStrategy = RotationStrategy.Size,
                    MaxFileSizeBytes = 10 * 1024 * 1024 // 10MB
                }, LogLevel.Error);
            });
            
            // Example 3: Register a factory that creates loggers with both console and file output
            services.AddSingleton<Func<string, IAgctorLogger>>(sp =>
            {
                return (category) =>
                {
                    // Create a list of loggers
                    var loggers = new List<IAgctorLogger> 
                    {
                        new ConsoleLogger(category),
                        new FileLogger(category, new FileLoggerOptions
                        {
                            LogDirectory = "logs",
                            FileName = "{category}-{date}.log"
                        })
                    };
                    
                    // Use LoggerFactory to create a composite logger (AggregateLogger is internal)
                    return LoggerFactory.CreateCompositeLogger(category, loggers);
                };
            });
            
            // Build service provider
            var serviceProvider = services.BuildServiceProvider();
            
            // Get a logger (Example 1)
            var logger = serviceProvider.GetRequiredService<IAgctorLogger>();
            logger.Info("This is logged to the application log file");
            
            // Get a logger factory (Example 3)
            var loggerFactory = serviceProvider.GetRequiredService<Func<string, IAgctorLogger>>();
            var componentLogger = loggerFactory("MyComponent");
            componentLogger.Info("This is logged to both console and MyComponent log file");
            #endif
        }
        
        /// <summary>
        /// Example showing how to use the LoggerFactory directly.
        /// </summary>
        public static void UseLoggerFactoryDirectly()
        {
            #if false // Examples are for documentation only, disabled at compile time
            // Reset the logger factory to start fresh
            LoggerFactory.ClearProviders();
            
            // Add console logger
            LoggerFactory.AddProvider(new ConsoleLoggerProvider(LogLevel.Info, true));
            
            // Add file logger for all logs
            LoggerFactory.AddFileLogger(new FileLoggerOptions
            {
                LogDirectory = "logs",
                FileName = "agctor-{date}.log",
                RotationStrategy = RotationStrategy.Daily
            });
            
            // Add separate file logger for errors only
            LoggerFactory.AddFileLogger(new FileLoggerOptions
            {
                LogDirectory = "logs/errors",
                FileName = "errors-{date}.log",
                RotationStrategy = RotationStrategy.Daily
            }, LogLevel.Error);
            
            // Get a logger
            var logger = LoggerFactory.CreateLogger("ExampleComponent");
            
            // Log messages
            logger.Info("This goes to console and main log file");
            logger.Error("This goes to console, main log file, and error log file");
            #endif
        }
        
        /// <summary>
        /// Example showing advanced file logger configuration.
        /// </summary>
        public static void ConfigureFileLoggerAdvanced()
        {
            #if false // Examples are for documentation only, disabled at compile time
            // Configure with retention policies
            var options = new FileLoggerOptions
            {
                LogDirectory = "logs/system",
                FileName = "system-{date}.log",
                RotationStrategy = RotationStrategy.Daily,
                MaxDaysToKeep = 90,      // Keep logs for 90 days
                MaxLogFiles = 100,       // Keep at most 100 log files
                IncludeTimestamps = true // Include timestamps in log entries
            };
            
            // Size-based rotation
            var sizeOptions = new FileLoggerOptions
            {
                LogDirectory = "logs/transactions",
                FileName = "tx-{category}.log",
                UseTimestampInFilename = false, // Don't include date in filename
                RotationStrategy = RotationStrategy.Size,
                MaxFileSizeBytes = 50 * 1024 * 1024 // 50MB per file
            };
            
            // Hourly rotation for high-volume logs
            var hourlyOptions = new FileLoggerOptions
            {
                LogDirectory = "logs/metrics",
                FileName = "metrics-{date}.log",
                RotationStrategy = RotationStrategy.Hourly
            };
            
            // Add to logger factory
            LoggerFactory.AddFileLogger(options);
            LoggerFactory.AddFileLogger(sizeOptions);
            LoggerFactory.AddFileLogger(hourlyOptions);
            
            // Create specialized loggers
            var systemLogger = LoggerFactory.CreateLogger("System");
            var transactionLogger = LoggerFactory.CreateLogger("Transactions");
            var metricsLogger = LoggerFactory.CreateLogger("Metrics");
            
            systemLogger.Info("System log entry");
            transactionLogger.Info("Transaction log entry");
            metricsLogger.Info("Metrics log entry");
            #endif
        }
        
        // Helper method to create a composite logger (this won't actually be called)
        private static IAgctorLogger CreateCompositeLogger(string category, List<IAgctorLogger> loggers)
        {
            // This is just a placeholder for the example
            return loggers[0];
        }
    }
} 