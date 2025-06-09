using System;
using System.Collections.Concurrent;

namespace AgctorSDK.Core.Utils.Logging
{
    /// <summary>
    /// Interface for logger providers to enable extensibility.
    /// </summary>
    public interface ILoggerProvider : IDisposable
    {
        /// <summary>
        /// Creates a new logger for the specified category.
        /// </summary>
        /// <param name="categoryName">The category name for the logger</param>
        /// <returns>A logger instance</returns>
        IAgctorLogger CreateLogger(string categoryName);
    }
    
    /// <summary>
    /// Provider for creating FileLogger instances.
    /// Supports creating and caching loggers by category.
    /// </summary>
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly FileLoggerOptions _options;
        private readonly LogLevel _minLevel;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new ConcurrentDictionary<string, FileLogger>();
        private bool _disposed;
        
        /// <summary>
        /// Initializes a new instance of the FileLoggerProvider.
        /// </summary>
        /// <param name="options">Configuration options for the file loggers</param>
        /// <param name="minLevel">Minimum log level to display</param>
        public FileLoggerProvider(FileLoggerOptions options, LogLevel minLevel = LogLevel.Info)
        {
            _options = options ?? new FileLoggerOptions();
            _minLevel = minLevel;
        }
        
        /// <summary>
        /// Creates a new logger for the specified category.
        /// </summary>
        /// <param name="categoryName">The category name for the logger</param>
        /// <returns>A logger instance for the specified category</returns>
        public IAgctorLogger CreateLogger(string categoryName)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FileLoggerProvider));
            }
            
            return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _options, _minLevel));
        }
        
        /// <summary>
        /// Disposes all loggers created by this provider.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var logger in _loggers.Values)
                {
                    logger.Dispose();
                }
                
                _loggers.Clear();
                _disposed = true;
            }
        }
    }
} 