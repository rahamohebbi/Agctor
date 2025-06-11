using System;
using System.Collections.Generic;
using AgctorSDK.Core.Utils.Logging;

namespace AgctorSDK.Core.IntegrationTests.Helpers
{
    /// <summary>
    /// Mock implementation of IAgctorLogger for testing purposes.
    /// </summary>
    public class MockAgctorLogger : IAgctorLogger
    {
        public List<string> LogEntries { get; } = new List<string>();
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Trace;

        public void Trace(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Trace))
            {
                LogEntries.Add($"TRACE: {FormatMessage(message, args)}");
            }
        }

        public void Debug(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Debug))
            {
                LogEntries.Add($"DEBUG: {FormatMessage(message, args)}");
            }
        }

        public void Info(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Info))
            {
                LogEntries.Add($"INFO: {FormatMessage(message, args)}");
            }
        }

        public void Warning(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Warning))
            {
                LogEntries.Add($"WARNING: {FormatMessage(message, args)}");
            }
        }

        public void Error(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Error))
            {
                LogEntries.Add($"ERROR: {FormatMessage(message, args)}");
            }
        }

        public void Error(Exception exception, string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Error))
            {
                LogEntries.Add($"ERROR: {FormatMessage(message, args)} | Exception: {exception.Message}");
            }
        }

        public void Critical(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                LogEntries.Add($"CRITICAL: {FormatMessage(message, args)}");
            }
        }

        public void Critical(Exception exception, string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                LogEntries.Add($"CRITICAL: {FormatMessage(message, args)} | Exception: {exception.Message}");
            }
        }

        public bool IsEnabled(LogLevel level)
        {
            return level >= MinimumLogLevel;
        }

        private string FormatMessage(string message, object[] args)
        {
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        public void Dispose()
        {
            // No resources to dispose
        }
    }
} 