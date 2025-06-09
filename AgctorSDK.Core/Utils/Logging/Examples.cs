using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

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
        
        /// <summary>
        /// Example of basic logging usage.
        /// </summary>
        public static void BasicLogging()
        {
            // Create a logger with default settings
            var logger = LoggerFactory.CreateLogger("ExampleLogger");
            
            // Log messages at different levels
            logger.Trace("This is a trace message");
            logger.Debug("Debug message with value: {0}", 42);
            logger.Info("Information: System starting");
            logger.Warning("Warning! Resource utilization at {0}%", 85);
            logger.Error("An error occurred: {0}", "Connection failed");
            logger.Critical("Critical failure in module {0}", "Authentication");
            
            try
            {
                throw new InvalidOperationException("Example exception");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error with exception details");
            }
        }
        
        /// <summary>
        /// Example of configuring daily log rotation.
        /// </summary>
        public static void DailyLogRotation()
        {
            // Configure file logger with daily rotation
            var options = new FileLoggerOptions
            {
                LogDirectory = "logs/daily",
                FileName = "app-{date}.log",
                RotationStrategy = RotationStrategy.Daily,
                MaxDaysToKeep = 30,
                MaxLogFiles = 100
            };
            
            // Add file logger to the factory
            LoggerFactory.AddFileLogger(options);
            
            // Create a logger using the factory
            var logger = LoggerFactory.CreateLogger("DailyRotationExample");
            
            // Log some messages
            logger.Info("Starting application with daily log rotation");
            logger.Debug("Configuration loaded successfully");
        }
        
        /// <summary>
        /// Example of configuring size-based log rotation with compression.
        /// </summary>
        public static void SizeBasedRotationWithCompression()
        {
            // Configure file logger with size-based rotation and compression
            var options = new FileLoggerOptions
            {
                LogDirectory = "logs/size_based",
                FileName = "app-{category}-{date}-{time}.log",
                RotationStrategy = RotationStrategy.Size,
                MaxFileSizeBytes = 1024 * 1024, // 1 MB
                CompressionStrategy = CompressionStrategy.OnRotation,
                ArchiveDirectoryStructure = ArchiveDirectoryStructure.ByYearMonth,
                MaxTotalSizeBytes = 100 * 1024 * 1024, // 100 MB max total size
                UseBackgroundWorker = true // Process logs in background thread
            };
            
            // Add file logger to the factory
            LoggerFactory.AddFileLogger(options);
            
            // Create a logger using the factory
            var logger = LoggerFactory.CreateLogger("SizeRotationExample");
            
            // Log some messages
            logger.Info("Starting application with size-based log rotation and compression");
            logger.Debug("This log will rotate when it reaches 1 MB");
            logger.Info("Compressed log files will be stored in year/month folders");
        }
        
        /// <summary>
        /// Example of using multiple loggers with different configurations.
        /// </summary>
        public static void MultipleLoggers()
        {
            // Configure debug log file (keeps all log levels, but rotates hourly)
            var debugOptions = new FileLoggerOptions
            {
                LogDirectory = "logs/debug",
                FileName = "debug-{date}.log",
                RotationStrategy = RotationStrategy.Hourly,
                MaxDaysToKeep = 2, // Only keep 2 days of debug logs
                CompressionStrategy = CompressionStrategy.OnCleanup,
                CleanupTime = new TimeSpan(3, 0, 0) // Cleanup at 3 AM
            };
            
            // Configure production log file (only warnings and above, daily rotation)
            var prodOptions = new FileLoggerOptions
            {
                LogDirectory = "logs/prod",
                FileName = "prod-{date}.log",
                RotationStrategy = RotationStrategy.Daily,
                MaxDaysToKeep = 90, // Keep 90 days of production logs
                CompressionStrategy = CompressionStrategy.OnRotation,
                ArchiveDirectoryStructure = ArchiveDirectoryStructure.ByYear,
                IncludeStatisticsOnRotation = true
            };
            
            // Add both loggers
            LoggerFactory.AddFileLogger(debugOptions, LogLevel.Trace); // Lower threshold for debug logger
            LoggerFactory.AddFileLogger(prodOptions, LogLevel.Warning); // Higher threshold for production logger
            
            // Create a logger that will write to both files based on the level
            var logger = LoggerFactory.CreateLogger("MultiLogger");
            
            // Debug message only goes to debug log
            logger.Debug("This only appears in the debug log");
            
            // Warning message goes to both logs
            logger.Warning("This appears in both logs");
        }
        
        /// <summary>
        /// Example of configuring weekly log rotation with statistics.
        /// </summary>
        public static void WeeklyRotationWithStatistics()
        {
            var options = new FileLoggerOptions
            {
                LogDirectory = "logs/weekly",
                FileName = "weekly-{date}.log",
                RotationStrategy = RotationStrategy.Weekly,
                MaxLogFiles = 52, // Keep up to a year of weekly logs
                IncludeStatisticsOnRotation = true, // Include stats summary at end of each weekly log
                CompressionStrategy = CompressionStrategy.OnRotation,
                CompressedFileExtension = ".zip" // Use .zip instead of default .gz
            };
            
            LoggerFactory.AddFileLogger(options);
            var logger = LoggerFactory.CreateLogger("WeeklyStats");
            
            logger.Info("Weekly log rotation with statistics enabled");
            logger.Info("Log will include message count per level when rotated");
        }
        
        /// <summary>
        /// Example of running a log-intensive process with the background worker.
        /// </summary>
        public static async Task LogIntensiveProcessAsync()
        {
            var options = new FileLoggerOptions
            {
                LogDirectory = "logs/performance",
                FileName = "perf-{date}.log",
                UseBackgroundWorker = true,
                MaxQueueSize = 100000, // Allow up to 100K messages in queue
                RotationStrategy = RotationStrategy.Size,
                MaxFileSizeBytes = 5 * 1024 * 1024 // 5 MB files
            };
            
            LoggerFactory.AddFileLogger(options);
            var logger = LoggerFactory.CreateLogger("PerformanceTest");
            
            logger.Info("Starting intensive logging process");
            
            // Simulate intensive logging
            for (int i = 0; i < 10000; i++)
            {
                logger.Debug("Processing item {0}", i);
                
                if (i % 1000 == 0)
                {
                    logger.Info("Milestone reached: {0} items processed", i);
                }
                
                if (i % 100 == 0)
                {
                    // Simulate some work
                    await Task.Delay(1);
                }
            }
            
            logger.Info("Intensive logging process completed");
        }
        
        /// <summary>
        /// Example of monthly log rotation for accounting/audit logs.
        /// </summary>
        public static void MonthlyAuditLogs()
        {
            var options = new FileLoggerOptions
            {
                LogDirectory = "logs/audit",
                FileName = "audit-{date}.log",
                RotationStrategy = RotationStrategy.Monthly,
                MaxDaysToKeep = 365 * 2, // Keep 2 years of audit logs
                CompressionStrategy = CompressionStrategy.OnRotation,
                ArchiveDirectoryStructure = ArchiveDirectoryStructure.ByYear
            };
            
            LoggerFactory.AddFileLogger(options);
            var logger = LoggerFactory.CreateLogger("AuditLog");
            
            logger.Info("User authenticated: userId={0}", "user123");
            logger.Info("Resource accessed: resourceId={0}, userId={1}", "doc-456", "user123");
        }
        
        // Helper method to create a composite logger (this won't actually be called)
        private static IAgctorLogger CreateCompositeLogger(string category, List<IAgctorLogger> loggers)
        {
            // This is just a placeholder for the example
            return loggers[0];
        }
    }
} 