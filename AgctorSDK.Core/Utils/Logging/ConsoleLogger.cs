using System;
using System.Globalization;

namespace AgctorSDK.Core.Utils.Logging
{
    /// <summary>
    /// Console implementation of the IAgctorLogger interface.
    /// Logs messages to the console with appropriate formatting and coloring.
    /// </summary>
    public class ConsoleLogger : IAgctorLogger
    {
        private readonly string _category;
        private readonly LogLevel _minLevel;
        private readonly bool _includeTimestamps;
        
        /// <summary>
        /// Initializes a new instance of the ConsoleLogger.
        /// </summary>
        /// <param name="category">Category name for the logger (typically a class or component name)</param>
        /// <param name="minLevel">Minimum log level to display</param>
        /// <param name="includeTimestamps">Whether to include timestamps in log messages</param>
        public ConsoleLogger(string category, LogLevel minLevel = LogLevel.Info, bool includeTimestamps = true)
        {
            _category = category ?? "Unknown";
            _minLevel = minLevel;
            _includeTimestamps = includeTimestamps;
        }
        
        /// <inheritdoc />
        public void Trace(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Trace))
            {
                WriteMessage(LogLevel.Trace, null, message, args);
            }
        }
        
        /// <inheritdoc />
        public void Debug(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Debug))
            {
                WriteMessage(LogLevel.Debug, null, message, args);
            }
        }
        
        /// <inheritdoc />
        public void Info(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Info))
            {
                WriteMessage(LogLevel.Info, null, message, args);
            }
        }
        
        /// <inheritdoc />
        public void Warning(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Warning))
            {
                WriteMessage(LogLevel.Warning, null, message, args);
            }
        }
        
        /// <inheritdoc />
        public void Error(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Error))
            {
                WriteMessage(LogLevel.Error, null, message, args);
            }
        }
        
        /// <inheritdoc />
        public void Error(Exception exception, string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Error))
            {
                WriteMessage(LogLevel.Error, exception, message, args);
            }
        }
        
        /// <inheritdoc />
        public void Critical(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                WriteMessage(LogLevel.Critical, null, message, args);
            }
        }
        
        /// <inheritdoc />
        public void Critical(Exception exception, string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                WriteMessage(LogLevel.Critical, exception, message, args);
            }
        }
        
        /// <inheritdoc />
        public bool IsEnabled(LogLevel level)
        {
            return level >= _minLevel;
        }
        
        /// <summary>
        /// Writes a formatted message to the console with appropriate coloring.
        /// </summary>
        private void WriteMessage(LogLevel level, Exception? exception, string message, object[] args)
        {
            // Format the message with arguments
            string formattedMessage = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, message, args) : message;
            
            // Build the full log message
            string timestamp = _includeTimestamps ? $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] " : "";
            string levelText = level switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT ",
                _ => "?????",
            };
            
            string fullMessage = $"{timestamp}[{levelText}] [{_category}] {formattedMessage}";
            
            // Set console color based on log level
            ConsoleColor originalColor = Console.ForegroundColor;
            
            Console.ForegroundColor = level switch
            {
                LogLevel.Trace => ConsoleColor.Gray,
                LogLevel.Debug => ConsoleColor.DarkGray,
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.DarkRed,
                _ => ConsoleColor.White,
            };
            
            // Write the message - protect against ObjectDisposed when Console is redirected by test host
            try
            {
                Console.WriteLine(fullMessage);
            }
            catch (ObjectDisposedException)
            {
                // Swallow; occurs in test environments where Console.Out has been disposed
            }
            
            // Write exception details if present
            if (exception != null)
            {
                try
                {
                    Console.WriteLine($"  Exception: {exception.GetType().Name}: {exception.Message}");
                    Console.WriteLine($"  StackTrace: {exception.StackTrace}");
                    
                    if (exception.InnerException != null)
                    {
                        Console.WriteLine($"  Inner Exception: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Ignore
                }
            }
            
            // Restore original color
            Console.ForegroundColor = originalColor;
            
            // Log to TestContext if available (for integration tests)
            try
            {
                var testContextType = Type.GetType("AgctorSDK.Core.IntegrationTests.TestHelpers.TestDependencies, AgctorSDK.Core.IntegrationTests");
                if (testContextType != null)
                {
                    var testContextProperty = testContextType.GetProperty("TestContext");
                    if (testContextProperty != null)
                    {
                        var testContext = testContextProperty.GetValue(null);
                        if (testContext != null)
                        {
                            var writeLineMethod = testContext.GetType().GetMethod("WriteLine", new[] { typeof(string) });
                            if (writeLineMethod != null)
                            {
                                writeLineMethod.Invoke(testContext, new[] { fullMessage });
                                
                                if (exception != null)
                                {
                                    writeLineMethod.Invoke(testContext, new[] { $"  Exception: {exception.GetType().Name}: {exception.Message}" });
                                    writeLineMethod.Invoke(testContext, new[] { $"  StackTrace: {exception.StackTrace}" });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore any errors in test logging
            }
        }
    }
} 