using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Utils.Logging
{
    /// <summary>
    /// Factory for creating logger instances.
    /// Centralizes logger creation and configuration.
    /// </summary>
    public static class LoggerFactory
    {
        private static LogLevel _defaultMinLevel = LogLevel.Info;
        private static bool _includeTimestamps = true;
        private static readonly List<ILoggerProvider> _providers = new List<ILoggerProvider>();
        
        static LoggerFactory()
        {
            // Add the default console logger provider
            AddProvider(new ConsoleLoggerProvider(_defaultMinLevel, _includeTimestamps));
        }
        
        /// <summary>
        /// Sets the default minimum log level for all loggers created by this factory.
        /// </summary>
        /// <param name="level">The minimum log level</param>
        public static void SetDefaultMinLevel(LogLevel level)
        {
            _defaultMinLevel = level;
        }
        
        /// <summary>
        /// Sets whether timestamps should be included in log messages.
        /// </summary>
        /// <param name="include">Whether to include timestamps</param>
        public static void SetIncludeTimestamps(bool include)
        {
            _includeTimestamps = include;
        }
        
        /// <summary>
        /// Adds a logger provider to the factory.
        /// </summary>
        /// <param name="provider">The provider to add</param>
        public static void AddProvider(ILoggerProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            
            _providers.Add(provider);
        }
        
        /// <summary>
        /// Adds a file logger provider with the specified options.
        /// </summary>
        /// <param name="options">Configuration options for the file logger</param>
        /// <param name="minLevel">Minimum log level (defaults to the factory default)</param>
        public static void AddFileLogger(FileLoggerOptions options, LogLevel? minLevel = null)
        {
            AddProvider(new FileLoggerProvider(options, minLevel ?? _defaultMinLevel));
        }
        
        /// <summary>
        /// Creates a new logger instance for the specified category.
        /// </summary>
        /// <param name="category">The category name for the logger</param>
        /// <param name="minLevel">Optional minimum log level (defaults to the factory default)</param>
        /// <returns>A new logger instance</returns>
        public static IAgctorLogger CreateLogger(string category, LogLevel? minLevel = null)
        {
            if (_providers.Count == 0)
            {
                // Add default console logger if no providers
                AddProvider(new ConsoleLoggerProvider(_defaultMinLevel, _includeTimestamps));
            }
            
            if (_providers.Count == 1)
            {
                // Optimize for the common case of a single provider
                return _providers[0].CreateLogger(category);
            }
            
            // Create an aggregate logger that forwards to all providers
            var loggers = _providers.Select(p => p.CreateLogger(category)).ToList();
            return new AggregateLogger(category, loggers);
        }
        
        /// <summary>
        /// Creates a composite logger that forwards messages to multiple loggers.
        /// </summary>
        /// <param name="category">The category name for the logger</param>
        /// <param name="loggers">The collection of loggers to forward to</param>
        /// <returns>A composite logger that forwards to all provided loggers</returns>
        public static IAgctorLogger CreateCompositeLogger(string category, IEnumerable<IAgctorLogger> loggers)
        {
            return new AggregateLogger(category, loggers.ToList());
        }
        
        /// <summary>
        /// Creates a new logger instance for the specified type.
        /// Uses the type name as the category name.
        /// </summary>
        /// <typeparam name="T">The type to create a logger for</typeparam>
        /// <param name="minLevel">Optional minimum log level (defaults to the factory default)</param>
        /// <returns>A new logger instance</returns>
        public static IAgctorLogger CreateLogger<T>(LogLevel? minLevel = null)
        {
            return CreateLogger(typeof(T).Name, minLevel);
        }
        
        /// <summary>
        /// Creates a new logger instance for the specified type.
        /// Uses the type name as the category name.
        /// </summary>
        /// <param name="type">The type to create a logger for</param>
        /// <param name="minLevel">Optional minimum log level (defaults to the factory default)</param>
        /// <returns>A new logger instance</returns>
        public static IAgctorLogger CreateLogger(Type type, LogLevel? minLevel = null)
        {
            return CreateLogger(type.Name, minLevel);
        }
        
        /// <summary>
        /// Clears all providers and disposes them.
        /// </summary>
        public static void ClearProviders()
        {
            foreach (var provider in _providers)
            {
                provider.Dispose();
            }
            
            _providers.Clear();
        }
    }
    
    /// <summary>
    /// Provider for creating ConsoleLogger instances.
    /// </summary>
    public class ConsoleLoggerProvider : ILoggerProvider
    {
        private readonly LogLevel _minLevel;
        private readonly bool _includeTimestamps;
        
        /// <summary>
        /// Initializes a new instance of the ConsoleLoggerProvider.
        /// </summary>
        /// <param name="minLevel">Minimum log level to display</param>
        /// <param name="includeTimestamps">Whether to include timestamps in log messages</param>
        public ConsoleLoggerProvider(LogLevel minLevel, bool includeTimestamps)
        {
            _minLevel = minLevel;
            _includeTimestamps = includeTimestamps;
        }
        
        /// <summary>
        /// Creates a new logger for the specified category.
        /// </summary>
        /// <param name="categoryName">The category name for the logger</param>
        /// <returns>A logger instance for the specified category</returns>
        public IAgctorLogger CreateLogger(string categoryName)
        {
            return new ConsoleLogger(categoryName, _minLevel, _includeTimestamps);
        }
        
        /// <summary>
        /// Disposes the provider.
        /// </summary>
        public void Dispose()
        {
            // No resources to dispose
        }
    }
    
    /// <summary>
    /// Logger that aggregates multiple loggers and forwards calls to all of them.
    /// </summary>
    internal class AggregateLogger : IAgctorLogger
    {
        private readonly string _category;
        private readonly IList<IAgctorLogger> _loggers;
        
        /// <summary>
        /// Initializes a new instance of the AggregateLogger.
        /// </summary>
        /// <param name="category">Category name for the logger</param>
        /// <param name="loggers">The loggers to aggregate</param>
        public AggregateLogger(string category, IList<IAgctorLogger> loggers)
        {
            _category = category;
            _loggers = loggers;
        }
        
        /// <inheritdoc />
        public void Trace(string message, params object[] args)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.Trace(message, args);
                }
            }
        }
        
        /// <inheritdoc />
        public void Debug(string message, params object[] args)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.Debug(message, args);
                }
            }
        }
        
        /// <inheritdoc />
        public void Info(string message, params object[] args)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(LogLevel.Info))
                {
                    logger.Info(message, args);
                }
            }
        }
        
        /// <inheritdoc />
        public void Warning(string message, params object[] args)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.Warning(message, args);
                }
            }
        }
        
        /// <inheritdoc />
        public void Error(string message, params object[] args)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.Error(message, args);
                }
            }
        }
        
        /// <inheritdoc />
        public void Error(Exception exception, string message, params object[] args)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.Error(exception, message, args);
                }
            }
        }
        
        /// <inheritdoc />
        public void Critical(string message, params object[] args)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(LogLevel.Critical))
                {
                    logger.Critical(message, args);
                }
            }
        }
        
        /// <inheritdoc />
        public void Critical(Exception exception, string message, params object[] args)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(LogLevel.Critical))
                {
                    logger.Critical(exception, message, args);
                }
            }
        }
        
        /// <inheritdoc />
        public bool IsEnabled(LogLevel level)
        {
            return _loggers.Any(logger => logger.IsEnabled(level));
        }
    }
} 