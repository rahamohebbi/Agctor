using System;

namespace AgctorSDK.Core.Utils.Logging
{
    /// <summary>
    /// Simple console logger implementation for Agctor framework.
    /// </summary>
    public class AgctorConsoleLogger : IAgctorLogger
    {
        /// <summary>
        /// Gets or sets the minimum log level. Messages below this level will not be logged.
        /// </summary>
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Info;

        /// <summary>
        /// Creates a new instance of the console logger.
        /// </summary>
        public AgctorConsoleLogger()
        {
        }

        /// <summary>
        /// Creates a new instance of the console logger with a specific minimum log level.
        /// </summary>
        public AgctorConsoleLogger(LogLevel minimumLogLevel)
        {
            MinimumLogLevel = minimumLogLevel;
        }

        /// <summary>
        /// Logs a trace message.
        /// </summary>
        public void Trace(string message)
        {
            if (IsEnabled(LogLevel.Trace))
            {
                WriteLog("TRACE", message, ConsoleColor.Gray);
            }
        }

        /// <summary>
        /// Logs a trace message with format parameters.
        /// </summary>
        public void Trace(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Trace))
            {
                WriteLog("TRACE", string.Format(message, args), ConsoleColor.Gray);
            }
        }

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        public void Debug(string message)
        {
            if (IsEnabled(LogLevel.Debug))
            {
                WriteLog("DEBUG", message, ConsoleColor.Cyan);
            }
        }

        /// <summary>
        /// Logs a debug message with format parameters.
        /// </summary>
        public void Debug(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Debug))
            {
                WriteLog("DEBUG", string.Format(message, args), ConsoleColor.Cyan);
            }
        }

        /// <summary>
        /// Logs an info message.
        /// </summary>
        public void Info(string message)
        {
            if (IsEnabled(LogLevel.Info))
            {
                WriteLog("INFO", message, ConsoleColor.White);
            }
        }

        /// <summary>
        /// Logs an info message with format parameters.
        /// </summary>
        public void Info(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Info))
            {
                WriteLog("INFO", string.Format(message, args), ConsoleColor.White);
            }
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public void Warning(string message)
        {
            if (IsEnabled(LogLevel.Warning))
            {
                WriteLog("WARN", message, ConsoleColor.Yellow);
            }
        }

        /// <summary>
        /// Logs a warning message with format parameters.
        /// </summary>
        public void Warning(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Warning))
            {
                WriteLog("WARN", string.Format(message, args), ConsoleColor.Yellow);
            }
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        public void Error(string message)
        {
            if (IsEnabled(LogLevel.Error))
            {
                WriteLog("ERROR", message, ConsoleColor.Red);
            }
        }

        /// <summary>
        /// Logs an error message with format parameters.
        /// </summary>
        public void Error(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Error))
            {
                WriteLog("ERROR", string.Format(message, args), ConsoleColor.Red);
            }
        }

        /// <summary>
        /// Logs an error message with exception details.
        /// </summary>
        public void Error(Exception exception, string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Error))
            {
                var formattedMessage = string.Format(message, args);
                WriteLog("ERROR", $"{formattedMessage}\nException: {exception.Message}\nStackTrace: {exception.StackTrace}", ConsoleColor.Red);
            }
        }

        /// <summary>
        /// Logs a critical message.
        /// </summary>
        public void Critical(string message)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                WriteLog("CRIT", message, ConsoleColor.Magenta);
            }
        }

        /// <summary>
        /// Logs a critical message with format parameters.
        /// </summary>
        public void Critical(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                WriteLog("CRIT", string.Format(message, args), ConsoleColor.Magenta);
            }
        }

        /// <summary>
        /// Logs a critical message with exception details.
        /// </summary>
        public void Critical(Exception exception, string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                var formattedMessage = string.Format(message, args);
                WriteLog("CRIT", $"{formattedMessage}\nException: {exception.Message}\nStackTrace: {exception.StackTrace}", ConsoleColor.Magenta);
            }
        }

        /// <summary>
        /// Gets whether logging at the specified level is enabled.
        /// </summary>
        public bool IsEnabled(LogLevel level)
        {
            return level >= MinimumLogLevel;
        }

        private void WriteLog(string level, string message, ConsoleColor color)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var originalColor = Console.ForegroundColor;
            
            Console.ForegroundColor = color;
            Console.WriteLine($"[{timestamp}] [{level}] {message}");
            Console.ForegroundColor = originalColor;
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        public void Dispose()
        {
            // No resources to dispose
        }
    }
} 