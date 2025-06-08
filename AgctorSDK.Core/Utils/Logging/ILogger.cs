using System;

namespace AgctorSDK.Core.Utils.Logging
{
    /// <summary>
    /// Defines the log levels for the application.
    /// </summary>
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Interface for logging service. Provides methods for logging messages at different levels.
    /// </summary>
    public interface IAgctorLogger
    {
        /// <summary>
        /// Logs a trace message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        void Trace(string message, params object[] args);
        
        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        void Debug(string message, params object[] args);
        
        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        void Info(string message, params object[] args);
        
        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        void Warning(string message, params object[] args);
        
        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        void Error(string message, params object[] args);
        
        /// <summary>
        /// Logs an error message with exception details.
        /// </summary>
        /// <param name="exception">The exception that occurred</param>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        void Error(Exception exception, string message, params object[] args);
        
        /// <summary>
        /// Logs a critical message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        void Critical(string message, params object[] args);
        
        /// <summary>
        /// Logs a critical message with exception details.
        /// </summary>
        /// <param name="exception">The exception that occurred</param>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        void Critical(Exception exception, string message, params object[] args);
        
        /// <summary>
        /// Gets whether logging at the specified level is enabled.
        /// </summary>
        /// <param name="level">The log level</param>
        /// <returns>True if logging at the specified level is enabled; otherwise, false.</returns>
        bool IsEnabled(LogLevel level);
    }
} 