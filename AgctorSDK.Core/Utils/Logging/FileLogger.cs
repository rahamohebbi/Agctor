using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Utils.Logging
{
    /// <summary>
    /// File-based implementation of the IAgctorLogger interface.
    /// Logs messages to a file with configurable rotation and formatting options.
    /// </summary>
    public class FileLogger : IAgctorLogger, IDisposable
    {
        private readonly string _category;
        private readonly LogLevel _minLevel;
        private readonly FileLoggerOptions _options;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private string _currentFilePath = string.Empty;
        private DateTime _currentFileDate;
        private long _currentFileSize;
        private bool _disposed;
        
        /// <summary>
        /// Initializes a new instance of the FileLogger.
        /// </summary>
        /// <param name="category">Category name for the logger (typically a class or component name)</param>
        /// <param name="options">Configuration options for the file logger</param>
        /// <param name="minLevel">Minimum log level to display</param>
        public FileLogger(string category, FileLoggerOptions options, LogLevel minLevel = LogLevel.Info)
        {
            _category = category ?? "Unknown";
            _options = options ?? new FileLoggerOptions();
            _minLevel = minLevel;
            _currentFileDate = DateTime.Today;
            
            // Initialize the log file
            InitializeLogFile();
        }
        
        /// <inheritdoc />
        public void Trace(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Trace))
            {
                WriteMessageAsync(LogLevel.Trace, null, message, args).GetAwaiter().GetResult();
            }
        }
        
        /// <inheritdoc />
        public void Debug(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Debug))
            {
                WriteMessageAsync(LogLevel.Debug, null, message, args).GetAwaiter().GetResult();
            }
        }
        
        /// <inheritdoc />
        public void Info(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Info))
            {
                WriteMessageAsync(LogLevel.Info, null, message, args).GetAwaiter().GetResult();
            }
        }
        
        /// <inheritdoc />
        public void Warning(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Warning))
            {
                WriteMessageAsync(LogLevel.Warning, null, message, args).GetAwaiter().GetResult();
            }
        }
        
        /// <inheritdoc />
        public void Error(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Error))
            {
                WriteMessageAsync(LogLevel.Error, null, message, args).GetAwaiter().GetResult();
            }
        }
        
        /// <inheritdoc />
        public void Error(Exception exception, string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Error))
            {
                WriteMessageAsync(LogLevel.Error, exception, message, args).GetAwaiter().GetResult();
            }
        }
        
        /// <inheritdoc />
        public void Critical(string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                WriteMessageAsync(LogLevel.Critical, null, message, args).GetAwaiter().GetResult();
            }
        }
        
        /// <inheritdoc />
        public void Critical(Exception exception, string message, params object[] args)
        {
            if (IsEnabled(LogLevel.Critical))
            {
                WriteMessageAsync(LogLevel.Critical, exception, message, args).GetAwaiter().GetResult();
            }
        }
        
        /// <inheritdoc />
        public bool IsEnabled(LogLevel level)
        {
            return level >= _minLevel;
        }
        
        /// <summary>
        /// Writes a formatted message to the log file with appropriate formatting.
        /// </summary>
        private async Task WriteMessageAsync(LogLevel level, Exception? exception, string message, object[] args)
        {
            if (_disposed) return;
            
            // Format the message with arguments
            string formattedMessage = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, message, args) : message;
            
            // Build the full log message
            string timestamp = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}]";
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
            
            var sb = new StringBuilder();
            sb.AppendLine($"{timestamp} [{levelText}] [{_category}] {formattedMessage}");
            
            // Add exception details if present
            if (exception != null)
            {
                sb.AppendLine($"  Exception: {exception.GetType().Name}: {exception.Message}");
                sb.AppendLine($"  StackTrace: {exception.StackTrace}");
                
                if (exception.InnerException != null)
                {
                    sb.AppendLine($"  Inner Exception: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
                }
            }
            
            // Check if we need to rotate the log file
            await CheckRotationAsync();
            
            // Write to the file
            await _lock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_currentFilePath, sb.ToString());
                _currentFileSize += sb.Length;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <summary>
        /// Initializes the log file, ensuring the directory exists and determining the current file path.
        /// </summary>
        private void InitializeLogFile()
        {
            // Ensure the directory exists
            string directory = _options.LogDirectory;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Determine the current file path
            _currentFilePath = GetLogFilePath();
            
            // Initialize the file size if the file exists
            if (File.Exists(_currentFilePath))
            {
                var fileInfo = new FileInfo(_currentFilePath);
                _currentFileSize = fileInfo.Length;
            }
            else
            {
                _currentFileSize = 0;
            }
        }
        
        /// <summary>
        /// Gets the path for the current log file based on the configuration options.
        /// </summary>
        private string GetLogFilePath()
        {
            string fileName = _options.FileName;
            
            // Apply date formatting if needed
            if (_options.UseTimestampInFilename)
            {
                string dateFormat = _options.RotationStrategy switch
                {
                    RotationStrategy.Daily => "yyyy-MM-dd",
                    RotationStrategy.Hourly => "yyyy-MM-dd-HH",
                    _ => "yyyy-MM-dd"
                };
                
                fileName = fileName.Replace("{date}", DateTime.Now.ToString(dateFormat));
            }
            
            // Apply category if needed
            fileName = fileName.Replace("{category}", _category.Replace(".", "-"));
            
            return Path.Combine(_options.LogDirectory, fileName);
        }
        
        /// <summary>
        /// Checks if the log file needs to be rotated based on the configured rotation strategy.
        /// </summary>
        private async Task CheckRotationAsync()
        {
            bool needsRotation = false;
            
            switch (_options.RotationStrategy)
            {
                case RotationStrategy.Size:
                    needsRotation = _currentFileSize >= _options.MaxFileSizeBytes;
                    break;
                
                case RotationStrategy.Daily:
                    needsRotation = DateTime.Today > _currentFileDate;
                    break;
                
                case RotationStrategy.Hourly:
                    needsRotation = DateTime.Now.Hour != _currentFileDate.Hour || DateTime.Today > _currentFileDate;
                    break;
            }
            
            if (needsRotation)
            {
                await _lock.WaitAsync();
                try
                {
                    // Update the date and reset size before getting a new path
                    _currentFileDate = DateTime.Now;
                    _currentFileSize = 0;
                    
                    // Get the new file path
                    _currentFilePath = GetLogFilePath();
                    
                    // Ensure the file exists
                    if (!File.Exists(_currentFilePath))
                    {
                        // Create the file with a header
                        await File.WriteAllTextAsync(_currentFilePath, 
                            $"--- Log file created at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}");
                        _currentFileSize = 50; // Approximate header size
                    }
                    
                    // Perform file cleanup if needed
                    await CleanupOldLogFilesAsync();
                }
                finally
                {
                    _lock.Release();
                }
            }
        }
        
        /// <summary>
        /// Cleans up old log files based on the configured retention policy.
        /// </summary>
        private async Task CleanupOldLogFilesAsync()
        {
            if (_options.MaxLogFiles <= 0 && _options.MaxDaysToKeep <= 0)
            {
                return; // No cleanup needed
            }
            
            try
            {
                // Get all log files in the directory
                string filePattern = _options.FileName.Replace("{date}", "*").Replace("{category}", "*");
                string[] logFiles = Directory.GetFiles(_options.LogDirectory, filePattern);
                
                // Delete old files based on date if configured
                if (_options.MaxDaysToKeep > 0)
                {
                    DateTime cutoffDate = DateTime.Now.AddDays(-_options.MaxDaysToKeep);
                    
                    foreach (string file in logFiles)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            if (fileInfo.LastWriteTime < cutoffDate)
                            {
                                File.Delete(file);
                            }
                        }
                        catch
                        {
                            // Ignore errors while deleting files
                        }
                    }
                }
                
                // Delete excess files based on count if configured
                if (_options.MaxLogFiles > 0)
                {
                    // Get files again (some may have been deleted above)
                    logFiles = Directory.GetFiles(_options.LogDirectory, filePattern);
                    
                    // Sort by last write time (oldest first)
                    Array.Sort(logFiles, (a, b) =>
                    {
                        return new FileInfo(a).LastWriteTime.CompareTo(new FileInfo(b).LastWriteTime);
                    });
                    
                    // Delete oldest files if we have too many
                    int excessCount = logFiles.Length - _options.MaxLogFiles;
                    for (int i = 0; i < excessCount; i++)
                    {
                        try
                        {
                            if (logFiles[i] != _currentFilePath) // Don't delete current file
                            {
                                File.Delete(logFiles[i]);
                            }
                        }
                        catch
                        {
                            // Ignore errors while deleting files
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors during cleanup
                await Task.CompletedTask; // Just to keep the method async
            }
        }
        
        /// <summary>
        /// Disposes resources used by the logger.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _lock.Dispose();
                _disposed = true;
            }
        }
    }
} 