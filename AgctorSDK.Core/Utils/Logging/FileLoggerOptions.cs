using System;

namespace AgctorSDK.Core.Utils.Logging
{
    /// <summary>
    /// Rotation strategies for log files.
    /// </summary>
    public enum RotationStrategy
    {
        /// <summary>
        /// No rotation, just use a single file.
        /// </summary>
        None,
        
        /// <summary>
        /// Rotate log files when they reach a specific size.
        /// </summary>
        Size,
        
        /// <summary>
        /// Create a new log file each day.
        /// </summary>
        Daily,
        
        /// <summary>
        /// Create a new log file each hour.
        /// </summary>
        Hourly
    }

    /// <summary>
    /// Configuration options for the FileLogger.
    /// </summary>
    public class FileLoggerOptions
    {
        /// <summary>
        /// Gets or sets the directory where log files will be stored.
        /// </summary>
        public string LogDirectory { get; set; } = "logs";
        
        /// <summary>
        /// Gets or sets the filename pattern for log files.
        /// Supports {date} and {category} placeholders.
        /// </summary>
        public string FileName { get; set; } = "agctor-{date}.log";
        
        /// <summary>
        /// Gets or sets whether to include timestamps in filenames.
        /// When true, {date} in FileName will be replaced with the current date.
        /// </summary>
        public bool UseTimestampInFilename { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the rotation strategy for log files.
        /// </summary>
        public RotationStrategy RotationStrategy { get; set; } = RotationStrategy.Daily;
        
        /// <summary>
        /// Gets or sets the maximum size in bytes for log files when using Size rotation.
        /// Default is 10MB.
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
        
        /// <summary>
        /// Gets or sets the maximum number of days to keep log files.
        /// Files older than this will be deleted during cleanup.
        /// Set to 0 to disable age-based cleanup.
        /// </summary>
        public int MaxDaysToKeep { get; set; } = 30;
        
        /// <summary>
        /// Gets or sets the maximum number of log files to keep.
        /// When exceeded, the oldest files will be deleted.
        /// Set to 0 to disable count-based cleanup.
        /// </summary>
        public int MaxLogFiles { get; set; } = 50;
        
        /// <summary>
        /// Gets or sets whether to include the timestamp in each log entry.
        /// </summary>
        public bool IncludeTimestamps { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the encoding to use for log files.
        /// </summary>
        public System.Text.Encoding Encoding { get; set; } = System.Text.Encoding.UTF8;
    }
} 