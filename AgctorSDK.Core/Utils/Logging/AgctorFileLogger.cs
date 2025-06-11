using System;

namespace AgctorSDK.Core.Utils.Logging
{
    /// <summary>
    /// Wrapper for FileLogger that implements IAgctorLogger.
    /// </summary>
    public class AgctorFileLogger : IAgctorLogger
    {
        private readonly FileLogger _fileLogger;

        /// <summary>
        /// Creates a new instance of the AgctorFileLogger.
        /// </summary>
        /// <param name="fileLogger">The underlying file logger</param>
        public AgctorFileLogger(FileLogger fileLogger)
        {
            _fileLogger = fileLogger ?? throw new ArgumentNullException(nameof(fileLogger));
        }

        /// <summary>
        /// Logs a trace message.
        /// </summary>
        public void Trace(string message)
        {
            _fileLogger.Trace(message);
        }

        /// <summary>
        /// Logs a trace message with format parameters.
        /// </summary>
        public void Trace(string message, params object[] args)
        {
            _fileLogger.Trace(message, args);
        }

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        public void Debug(string message)
        {
            _fileLogger.Debug(message);
        }

        /// <summary>
        /// Logs a debug message with format parameters.
        /// </summary>
        public void Debug(string message, params object[] args)
        {
            _fileLogger.Debug(message, args);
        }

        /// <summary>
        /// Logs an info message.
        /// </summary>
        public void Info(string message)
        {
            _fileLogger.Info(message);
        }

        /// <summary>
        /// Logs an info message with format parameters.
        /// </summary>
        public void Info(string message, params object[] args)
        {
            _fileLogger.Info(message, args);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public void Warning(string message)
        {
            _fileLogger.Warning(message);
        }

        /// <summary>
        /// Logs a warning message with format parameters.
        /// </summary>
        public void Warning(string message, params object[] args)
        {
            _fileLogger.Warning(message, args);
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        public void Error(string message)
        {
            _fileLogger.Error(message);
        }

        /// <summary>
        /// Logs an error message with format parameters.
        /// </summary>
        public void Error(string message, params object[] args)
        {
            _fileLogger.Error(message, args);
        }

        /// <summary>
        /// Logs an error message with exception details.
        /// </summary>
        public void Error(Exception exception, string message, params object[] args)
        {
            _fileLogger.Error(exception, message, args);
        }

        /// <summary>
        /// Logs a critical message.
        /// </summary>
        public void Critical(string message)
        {
            _fileLogger.Critical(message);
        }

        /// <summary>
        /// Logs a critical message with format parameters.
        /// </summary>
        public void Critical(string message, params object[] args)
        {
            _fileLogger.Critical(message, args);
        }

        /// <summary>
        /// Logs a critical message with exception details.
        /// </summary>
        public void Critical(Exception exception, string message, params object[] args)
        {
            _fileLogger.Critical(exception, message, args);
        }

        /// <summary>
        /// Gets whether logging at the specified level is enabled.
        /// </summary>
        public bool IsEnabled(LogLevel level)
        {
            return _fileLogger.IsEnabled(level);
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        public void Dispose()
        {
            _fileLogger.Dispose();
        }
    }
} 