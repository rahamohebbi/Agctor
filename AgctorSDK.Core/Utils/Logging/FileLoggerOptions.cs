using System;
using System.Text;

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
        Hourly,

        /// <summary>
        /// Create a new log file each week.
        /// </summary>
        Weekly,

        /// <summary>
        /// Create a new log file each month.
        /// </summary>
        Monthly
    }

    /// <summary>
    /// Compression strategies for log files.
    /// </summary>
    public enum CompressionStrategy
    {
        /// <summary>
        /// Do not compress log files.
        /// </summary>
        None,
        
        /// <summary>
        /// Compress log files when they are rotated.
        /// </summary>
        OnRotation,
        
        /// <summary>
        /// Compress log files during scheduled cleanup.
        /// </summary>
        OnCleanup
    }

    /// <summary>
    /// Archive directory structure options.
    /// </summary>
    public enum ArchiveDirectoryStructure
    {
        /// <summary>
        /// Store all archives in a single directory.
        /// </summary>
        Flat,
        
        /// <summary>
        /// Organize archives by year (archives/2023/).
        /// </summary>
        ByYear,
        
        /// <summary>
        /// Organize archives by year and month (archives/2023/01/).
        /// </summary>
        ByYearMonth
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
        /// Gets or sets the directory where archived log files will be stored.
        /// If null or empty, archives will be stored in {LogDirectory}/archives.
        /// </summary>
        public string ArchiveDirectory { get; set; } = "";
        
        /// <summary>
        /// Gets or sets the filename pattern for log files.
        /// Supports {date}, {time}, {pid}, and {category} placeholders.
        /// </summary>
        public string FileName { get; set; } = "agctor-{date}.log";
        
        /// <summary>
        /// Gets or sets whether to include timestamps in filenames.
        /// When true, {date} and {time} in FileName will be replaced with the current date/time.
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
        /// Gets or sets the total maximum size in bytes for all log files combined.
        /// When exceeded, the oldest files will be deleted.
        /// Set to 0 to disable size-based cleanup.
        /// Default is 1GB.
        /// </summary>
        public long MaxTotalSizeBytes { get; set; } = 1024 * 1024 * 1024;
        
        /// <summary>
        /// Gets or sets whether to include the timestamp in each log entry.
        /// </summary>
        public bool IncludeTimestamps { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the encoding to use for log files.
        /// </summary>
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        
        /// <summary>
        /// Gets or sets the compression strategy for log files.
        /// </summary>
        public CompressionStrategy CompressionStrategy { get; set; } = CompressionStrategy.None;
        
        /// <summary>
        /// Gets or sets the directory structure to use for archived log files.
        /// </summary>
        public ArchiveDirectoryStructure ArchiveDirectoryStructure { get; set; } = ArchiveDirectoryStructure.Flat;
        
        /// <summary>
        /// Gets or sets the file extension to use for compressed log files.
        /// </summary>
        public string CompressedFileExtension { get; set; } = ".gz";
        
        /// <summary>
        /// Gets or sets whether to include log statistics in rotated files.
        /// When true, a summary of log counts by level will be written at the end of each rotated file.
        /// </summary>
        public bool IncludeStatisticsOnRotation { get; set; } = false;
        
        /// <summary>
        /// Gets or sets the time of day to perform cleanup operations (in local time).
        /// Set to null to perform cleanup during any rotation.
        /// </summary>
        public TimeSpan? CleanupTime { get; set; } = null;
        
        /// <summary>
        /// Gets or sets whether to use a background worker for file operations.
        /// When true, file writes and rotations happen asynchronously to avoid blocking the application.
        /// </summary>
        public bool UseBackgroundWorker { get; set; } = false;
        
        /// <summary>
        /// Gets or sets the maximum queue size for the background worker.
        /// When exceeded, new log messages will be dropped.
        /// Only applicable when UseBackgroundWorker is true.
        /// </summary>
        public int MaxQueueSize { get; set; } = 10000;

        /// <summary>
        /// Gets the effective archive directory path.
        /// </summary>
        /// <returns>The full path to the archive directory</returns>
        public string GetArchiveDirectoryPath()
        {
            if (!string.IsNullOrEmpty(ArchiveDirectory))
            {
                return ArchiveDirectory;
            }
            
            return System.IO.Path.Combine(LogDirectory, "archives");
        }
    }
} 