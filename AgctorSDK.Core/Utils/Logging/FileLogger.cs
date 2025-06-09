using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        private Dictionary<LogLevel, int> _logLevelCounts = new Dictionary<LogLevel, int>();
        private DateTime _lastCleanupTime = DateTime.MinValue;
        
        // Background worker queue and cancellation token
        private ConcurrentQueue<LogEntry>? _backgroundQueue;
        private CancellationTokenSource? _backgroundCancellation;
        private Task? _backgroundTask;
        private int _droppedMessages = 0;
        
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
            _currentFileDate = DateTime.Now;
            
            // Initialize log level counts
            foreach (LogLevel level in Enum.GetValues(typeof(LogLevel)))
            {
                _logLevelCounts[level] = 0;
            }
            
            // Initialize the log file
            InitializeLogFile();
            
            // Initialize background worker if enabled
            if (_options.UseBackgroundWorker)
            {
                _backgroundQueue = new ConcurrentQueue<LogEntry>();
                _backgroundCancellation = new CancellationTokenSource();
                _backgroundTask = Task.Run(() => BackgroundWorker(_backgroundCancellation.Token));
            }
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
        /// Background worker method that processes log entries from the queue.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for stopping the background worker</param>
        private async Task BackgroundWorker(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Process all available log entries
                    while (_backgroundQueue?.TryDequeue(out LogEntry entry) == true)
                    {
                        await WriteMessageToFileAsync(entry.Level, entry.Exception, entry.Message);
                    }
                    
                    // Sleep for a short period before checking for more entries
                    await Task.Delay(50, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, exit the loop
                    break;
                }
                catch (Exception)
                {
                    // Ignore exceptions in the background worker
                    // We don't want to crash the application if logging fails
                    await Task.Delay(1000, cancellationToken); // Delay longer if we hit an error
                }
            }
            
            // Process any remaining entries when shutting down
            while (_backgroundQueue?.TryDequeue(out LogEntry entry) == true)
            {
                try
                {
                    await WriteMessageToFileAsync(entry.Level, entry.Exception, entry.Message);
                }
                catch
                {
                    // Ignore exceptions during shutdown
                }
            }
        }
        
        /// <summary>
        /// Writes a message to the log. If background worker is enabled, adds the message to the queue.
        /// </summary>
        private void WriteMessage(LogLevel level, Exception? exception, string message, object[] args)
        {
            // Format the message with arguments
            string formattedMessage = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, message, args) : message;
            
            // Increment the log level counter
            lock (_logLevelCounts)
            {
                _logLevelCounts[level]++;
            }
            
            if (_options.UseBackgroundWorker && _backgroundQueue != null)
            {
                // Add to the background queue
                if (_backgroundQueue.Count < _options.MaxQueueSize)
                {
                    _backgroundQueue.Enqueue(new LogEntry(level, exception, formattedMessage));
                }
                else
                {
                    // Queue is full, drop the message
                    Interlocked.Increment(ref _droppedMessages);
                    
                    // Log a warning if we're dropping messages (to the console only, to avoid recursion)
                    if (_droppedMessages == 1 || _droppedMessages % 1000 == 0)
                    {
                        Console.WriteLine($"WARNING: FileLogger queue is full. Dropped {_droppedMessages} messages.");
                    }
                }
            }
            else
            {
                // Write directly to the file
                WriteMessageAsync(level, exception, formattedMessage).GetAwaiter().GetResult();
            }
        }
        
        /// <summary>
        /// Writes a formatted message to the log file with appropriate formatting.
        /// </summary>
        private async Task WriteMessageAsync(LogLevel level, Exception? exception, string message)
        {
            if (_disposed) return;
            
            // Check if we need to rotate the log file
            await CheckRotationAsync();
            
            // Write to the file
            await WriteMessageToFileAsync(level, exception, message);
        }
        
        /// <summary>
        /// Writes a formatted message to the current log file.
        /// </summary>
        private async Task WriteMessageToFileAsync(LogLevel level, Exception? exception, string message)
        {
            if (_disposed) return;
            
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
            sb.AppendLine($"{timestamp} [{levelText}] [{_category}] {message}");
            
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
            
            // Write to the file
            await _lock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_currentFilePath, sb.ToString(), _options.Encoding);
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
            
            // Create archive directory if needed
            if (_options.CompressionStrategy != CompressionStrategy.None)
            {
                string archiveDir = _options.GetArchiveDirectoryPath();
                if (!Directory.Exists(archiveDir))
                {
                    Directory.CreateDirectory(archiveDir);
                }
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
                
                // Create the file with a header
                try
                {
                    string header = $"--- Log file created at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}";
                    File.WriteAllText(_currentFilePath, header, _options.Encoding);
                    _currentFileSize = header.Length;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating log file: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Gets the path for the current log file based on the configuration options.
        /// </summary>
        private string GetLogFilePath()
        {
            string fileName = _options.FileName;
            
            // Apply date and time formatting if needed
            if (_options.UseTimestampInFilename)
            {
                string dateFormat = _options.RotationStrategy switch
                {
                    RotationStrategy.Daily => "yyyy-MM-dd",
                    RotationStrategy.Hourly => "yyyy-MM-dd-HH",
                    RotationStrategy.Weekly => "yyyy-'week'ww",
                    RotationStrategy.Monthly => "yyyy-MM",
                    _ => "yyyy-MM-dd"
                };
                
                fileName = fileName.Replace("{date}", DateTime.Now.ToString(dateFormat));
                fileName = fileName.Replace("{time}", DateTime.Now.ToString("HH-mm-ss"));
                fileName = fileName.Replace("{pid}", Environment.ProcessId.ToString());
            }
            
            // Apply category if needed
            fileName = fileName.Replace("{category}", _category.Replace(".", "-"));
            
            return Path.Combine(_options.LogDirectory, fileName);
        }
        
        /// <summary>
        /// Gets the path for an archived log file.
        /// </summary>
        private string GetArchiveFilePath(string sourceFilePath)
        {
            string fileName = Path.GetFileName(sourceFilePath);
            string archiveDir = _options.GetArchiveDirectoryPath();
            
            // Apply directory structure if configured
            if (_options.ArchiveDirectoryStructure != ArchiveDirectoryStructure.Flat)
            {
                DateTime now = DateTime.Now;
                
                if (_options.ArchiveDirectoryStructure == ArchiveDirectoryStructure.ByYear)
                {
                    archiveDir = Path.Combine(archiveDir, now.Year.ToString());
                }
                else if (_options.ArchiveDirectoryStructure == ArchiveDirectoryStructure.ByYearMonth)
                {
                    archiveDir = Path.Combine(archiveDir, now.Year.ToString(), now.Month.ToString("00"));
                }
                
                // Ensure directory exists
                if (!Directory.Exists(archiveDir))
                {
                    Directory.CreateDirectory(archiveDir);
                }
            }
            
            // Add compression extension if needed
            if (_options.CompressionStrategy != CompressionStrategy.None)
            {
                fileName += _options.CompressedFileExtension;
            }
            
            return Path.Combine(archiveDir, fileName);
        }
        
        /// <summary>
        /// Checks if the log file needs to be rotated based on the configured rotation strategy.
        /// </summary>
        private async Task CheckRotationAsync()
        {
            bool needsRotation = false;
            bool performCleanup = false;
            
            switch (_options.RotationStrategy)
            {
                case RotationStrategy.Size:
                    needsRotation = _currentFileSize >= _options.MaxFileSizeBytes;
                    break;
                
                case RotationStrategy.Daily:
                    needsRotation = DateTime.Today > _currentFileDate.Date;
                    break;
                
                case RotationStrategy.Hourly:
                    needsRotation = DateTime.Now.Hour != _currentFileDate.Hour || DateTime.Today > _currentFileDate.Date;
                    break;
                
                case RotationStrategy.Weekly:
                    // Get week number
                    var currentWeek = CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                        DateTime.Now, CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
                    var fileWeek = CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                        _currentFileDate, CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
                    
                    needsRotation = currentWeek != fileWeek || DateTime.Now.Year != _currentFileDate.Year;
                    break;
                
                case RotationStrategy.Monthly:
                    needsRotation = DateTime.Now.Month != _currentFileDate.Month || DateTime.Now.Year != _currentFileDate.Year;
                    break;
            }
            
            // Check if we should perform cleanup based on scheduled time
            if (_options.CleanupTime.HasValue)
            {
                DateTime now = DateTime.Now;
                DateTime today = now.Date;
                DateTime scheduledTime = today.Add(_options.CleanupTime.Value);
                
                // If current time is past scheduled time and we haven't run cleanup today
                if (now >= scheduledTime && _lastCleanupTime.Date < today)
                {
                    performCleanup = true;
                    _lastCleanupTime = now;
                }
            }
            else
            {
                // If no specific cleanup time, perform during rotation
                performCleanup = needsRotation;
            }
            
            if (needsRotation)
            {
                await RotateLogFileAsync();
            }
            
            if (performCleanup)
            {
                await CleanupOldLogFilesAsync();
            }
        }
        
        /// <summary>
        /// Rotates the log file, closing the current one and creating a new one.
        /// </summary>
        private async Task RotateLogFileAsync()
        {
            string oldFilePath = _currentFilePath;
            
            await _lock.WaitAsync();
            try
            {
                // Only rotate if the file exists and has content
                if (File.Exists(oldFilePath) && _currentFileSize > 0)
                {
                    // Add statistics if configured
                    if (_options.IncludeStatisticsOnRotation)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"--- Log Statistics at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
                        foreach (var kvp in _logLevelCounts.OrderBy(k => k.Key))
                        {
                            sb.AppendLine($"  {kvp.Key}: {kvp.Value} messages");
                        }
                        sb.AppendLine("--- End of Log ---");
                        
                        await File.AppendAllTextAsync(oldFilePath, sb.ToString(), _options.Encoding);
                    }
                    
                    // Archive the file if compression is enabled
                    if (_options.CompressionStrategy == CompressionStrategy.OnRotation)
                    {
                        string archivePath = GetArchiveFilePath(oldFilePath);
                        await CompressFileAsync(oldFilePath, archivePath);
                        
                        // Delete the original file after compression
                        File.Delete(oldFilePath);
                    }
                }
                
                // Update the date and reset size before getting a new path
                _currentFileDate = DateTime.Now;
                _currentFileSize = 0;
                
                // Reset log level counts
                foreach (LogLevel level in Enum.GetValues(typeof(LogLevel)))
                {
                    _logLevelCounts[level] = 0;
                }
                
                // Get the new file path
                _currentFilePath = GetLogFilePath();
                
                // Ensure the file exists
                if (!File.Exists(_currentFilePath))
                {
                    // Create the file with a header
                    await File.WriteAllTextAsync(_currentFilePath, 
                        $"--- Log file created at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}", 
                        _options.Encoding);
                    _currentFileSize = 50; // Approximate header size
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rotating log file: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <summary>
        /// Compresses a log file and moves it to the archive directory.
        /// </summary>
        private async Task CompressFileAsync(string sourceFilePath, string destinationFilePath)
        {
            try
            {
                using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destinationStream = new FileStream(destinationFilePath, FileMode.Create))
                using (var gzipStream = new GZipStream(destinationStream, CompressionLevel.Optimal))
                {
                    await sourceStream.CopyToAsync(gzipStream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error compressing log file: {ex.Message}");
                
                // If compression fails, try to copy the file uncompressed
                try
                {
                    string uncompressedPath = destinationFilePath;
                    if (uncompressedPath.EndsWith(_options.CompressedFileExtension))
                    {
                        uncompressedPath = uncompressedPath.Substring(0, uncompressedPath.Length - _options.CompressedFileExtension.Length);
                    }
                    
                    File.Copy(sourceFilePath, uncompressedPath, true);
                }
                catch
                {
                    // Ignore copy errors
                }
            }
        }
        
        /// <summary>
        /// Cleans up old log files based on the configured retention policy.
        /// </summary>
        private async Task CleanupOldLogFilesAsync()
        {
            if (_options.MaxLogFiles <= 0 && _options.MaxDaysToKeep <= 0 && _options.MaxTotalSizeBytes <= 0)
            {
                return; // No cleanup needed
            }
            
            try
            {
                // Get all log files in the directory
                string filePattern = _options.FileName
                    .Replace("{date}", "*")
                    .Replace("{time}", "*")
                    .Replace("{pid}", "*")
                    .Replace("{category}", "*");
                
                List<FileInfo> logFiles = new List<FileInfo>();
                
                // Add files from main log directory
                logFiles.AddRange(new DirectoryInfo(_options.LogDirectory)
                    .GetFiles(filePattern)
                    .Where(f => f.FullName != _currentFilePath)); // Don't include current file
                
                // Add files from archive directory if it exists and different from log directory
                string archiveDir = _options.GetArchiveDirectoryPath();
                if (Directory.Exists(archiveDir) && !string.Equals(archiveDir, _options.LogDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    // Handle flat structure
                    if (_options.ArchiveDirectoryStructure == ArchiveDirectoryStructure.Flat)
                    {
                        logFiles.AddRange(new DirectoryInfo(archiveDir).GetFiles("*"));
                    }
                    // Handle hierarchical structure
                    else
                    {
                        foreach (var dir in Directory.GetDirectories(archiveDir, "*", SearchOption.AllDirectories))
                        {
                            logFiles.AddRange(new DirectoryInfo(dir).GetFiles("*"));
                        }
                    }
                }
                
                // Sort by last write time (oldest first)
                logFiles = logFiles.OrderBy(f => f.LastWriteTime).ToList();
                
                // Delete old files based on date if configured
                if (_options.MaxDaysToKeep > 0)
                {
                    DateTime cutoffDate = DateTime.Now.AddDays(-_options.MaxDaysToKeep);
                    
                    foreach (var file in logFiles.ToList())
                    {
                        if (file.LastWriteTime < cutoffDate)
                        {
                            try
                            {
                                file.Delete();
                                logFiles.Remove(file); // Remove from list after deletion
                            }
                            catch
                            {
                                // Ignore errors while deleting files
                            }
                        }
                    }
                }
                
                // Delete excess files based on count if configured
                if (_options.MaxLogFiles > 0 && logFiles.Count > _options.MaxLogFiles)
                {
                    int excessCount = logFiles.Count - _options.MaxLogFiles;
                    for (int i = 0; i < excessCount && i < logFiles.Count; i++)
                    {
                        try
                        {
                            logFiles[i].Delete();
                        }
                        catch
                        {
                            // Ignore errors while deleting files
                        }
                    }
                    
                    // Update the list after deletions
                    logFiles = logFiles.Skip(excessCount).ToList();
                }
                
                // Delete excess files based on total size if configured
                if (_options.MaxTotalSizeBytes > 0)
                {
                    long totalSize = logFiles.Sum(f => f.Length);
                    
                    // Add current file size
                    if (File.Exists(_currentFilePath))
                    {
                        totalSize += new FileInfo(_currentFilePath).Length;
                    }
                    
                    // Delete oldest files until we're under the size limit
                    for (int i = 0; i < logFiles.Count && totalSize > _options.MaxTotalSizeBytes; i++)
                    {
                        try
                        {
                            long fileSize = logFiles[i].Length;
                            logFiles[i].Delete();
                            totalSize -= fileSize;
                        }
                        catch
                        {
                            // Ignore errors while deleting files
                        }
                    }
                }
                
                // Compress files that should be compressed during cleanup
                if (_options.CompressionStrategy == CompressionStrategy.OnCleanup)
                {
                    // Find uncompressed files in the log directory (not current file)
                    var filesToCompress = new DirectoryInfo(_options.LogDirectory)
                        .GetFiles(filePattern)
                        .Where(f => f.FullName != _currentFilePath && 
                               !f.Extension.Equals(_options.CompressedFileExtension, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    foreach (var file in filesToCompress)
                    {
                        string archivePath = GetArchiveFilePath(file.FullName);
                        await CompressFileAsync(file.FullName, archivePath);
                        
                        // Delete the original file after compression
                        try
                        {
                            file.Delete();
                        }
                        catch
                        {
                            // Ignore deletion errors
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during log file cleanup: {ex.Message}");
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
                _disposed = true;
                
                // Stop the background worker if it's running
                if (_options.UseBackgroundWorker && _backgroundCancellation != null)
                {
                    _backgroundCancellation.Cancel();
                    try
                    {
                        // Wait for the background task to complete
                        _backgroundTask?.Wait(1000);
                    }
                    catch
                    {
                        // Ignore exceptions during shutdown
                    }
                    
                    _backgroundCancellation.Dispose();
                    _backgroundCancellation = null;
                }
                
                _lock.Dispose();
            }
        }
        
        /// <summary>
        /// Represents a log entry for the background queue.
        /// </summary>
        private class LogEntry
        {
            public LogLevel Level { get; }
            public Exception? Exception { get; }
            public string Message { get; }
            
            public LogEntry(LogLevel level, Exception? exception, string message)
            {
                Level = level;
                Exception = exception;
                Message = message;
            }
        }
    }
} 