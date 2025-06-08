using System;

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
        /// Creates a new logger instance for the specified category.
        /// </summary>
        /// <param name="category">The category name for the logger</param>
        /// <param name="minLevel">Optional minimum log level (defaults to the factory default)</param>
        /// <returns>A new logger instance</returns>
        public static IAgctorLogger CreateLogger(string category, LogLevel? minLevel = null)
        {
            return new ConsoleLogger(category, minLevel ?? _defaultMinLevel, _includeTimestamps);
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
    }
} 