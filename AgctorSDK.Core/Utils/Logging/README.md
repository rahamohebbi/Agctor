# AgctorSDK Logging System

This directory contains a configurable logging system for the AgctorSDK, offering flexible logging options with support for console and file outputs, log rotation, compression, and archiving.

## Key Features

- Multiple log outputs (console, file)
- Configurable log levels and formatting
- Log file rotation based on size, time periods (hourly, daily, weekly, monthly)
- Automatic log cleanup with configurable retention policies
- Log compression and archiving
- Background processing for high-volume logging
- Hierarchical archive organization

## Usage Examples

### Basic Usage

```csharp
// Get a logger with default settings (console output)
var logger = LoggerFactory.CreateLogger("MyComponent");

// Log at different levels
logger.Debug("Debug message");
logger.Info("Information: {0}", "Some info");
logger.Warning("Warning message");
logger.Error(exception, "An error occurred");
```

### File Logging with Daily Rotation

```csharp
// Configure file logger with daily rotation
var options = new FileLoggerOptions
{
    LogDirectory = "logs",
    FileName = "app-{date}.log",
    RotationStrategy = RotationStrategy.Daily,
    MaxDaysToKeep = 30,
    MaxLogFiles = 100
};

// Add file logger to the factory
LoggerFactory.AddFileLogger(options);

// Create a logger
var logger = LoggerFactory.CreateLogger("MyComponent");
logger.Info("Application started");
```

### Advanced Log Rotation and Compression

```csharp
// Configure size-based rotation with compression
var options = new FileLoggerOptions
{
    LogDirectory = "logs/app",
    FileName = "app-{category}-{date}.log",
    RotationStrategy = RotationStrategy.Size,
    MaxFileSizeBytes = 10 * 1024 * 1024, // 10 MB
    CompressionStrategy = CompressionStrategy.OnRotation,
    ArchiveDirectoryStructure = ArchiveDirectoryStructure.ByYearMonth,
    MaxTotalSizeBytes = 1024 * 1024 * 1024, // 1 GB total storage
    UseBackgroundWorker = true // Process logs in background thread
};

LoggerFactory.AddFileLogger(options);
```

### Multiple Log Files with Different Configurations

```csharp
// Debug logs (all levels, hourly rotation, short retention)
var debugOptions = new FileLoggerOptions
{
    LogDirectory = "logs/debug",
    FileName = "debug-{date}.log",
    RotationStrategy = RotationStrategy.Hourly,
    MaxDaysToKeep = 2,
    CompressionStrategy = CompressionStrategy.OnCleanup
};

// Production logs (warnings and above, daily rotation, longer retention)
var prodOptions = new FileLoggerOptions
{
    LogDirectory = "logs/prod",
    FileName = "prod-{date}.log",
    RotationStrategy = RotationStrategy.Daily,
    MaxDaysToKeep = 90,
    CompressionStrategy = CompressionStrategy.OnRotation,
    ArchiveDirectoryStructure = ArchiveDirectoryStructure.ByYear
};

// Add both loggers with different thresholds
LoggerFactory.AddFileLogger(debugOptions, LogLevel.Trace);
LoggerFactory.AddFileLogger(prodOptions, LogLevel.Warning);

// Create a logger that writes to both files based on level
var logger = LoggerFactory.CreateLogger("MyComponent");
```

## Configuration Options

### Log Levels

- `Trace`: Detailed debugging information
- `Debug`: Debugging information
- `Info`: General information
- `Warning`: Warning conditions
- `Error`: Error conditions
- `Critical`: Critical conditions

### Rotation Strategies

- `None`: No rotation, use a single file
- `Size`: Rotate when file reaches a specific size
- `Daily`: Create a new file each day
- `Hourly`: Create a new file each hour
- `Weekly`: Create a new file each week
- `Monthly`: Create a new file each month

### Compression Strategies

- `None`: Don't compress log files
- `OnRotation`: Compress files when they are rotated
- `OnCleanup`: Compress files during scheduled cleanup

### Archive Directory Structure

- `Flat`: Store all archives in a single directory
- `ByYear`: Organize archives by year (archives/2023/)
- `ByYearMonth`: Organize archives by year and month (archives/2023/01/)

### Filename Pattern Placeholders

- `{date}`: Current date (format depends on rotation strategy)
- `{time}`: Current time (HH-mm-ss)
- `{category}`: Logger category name
- `{pid}`: Process ID

## Advanced Features

### Background Processing

For high-volume logging scenarios, enable background processing to avoid blocking the application:

```csharp
var options = new FileLoggerOptions
{
    UseBackgroundWorker = true,
    MaxQueueSize = 100000 // Queue size before dropping messages
};
```

### Scheduled Cleanup

Configure a specific time for log cleanup operations:

```csharp
var options = new FileLoggerOptions
{
    CleanupTime = new TimeSpan(3, 0, 0) // Run cleanup at 3 AM
};
```

### Log Statistics

Include statistics about log counts when files are rotated:

```csharp
var options = new FileLoggerOptions
{
    IncludeStatisticsOnRotation = true
};
```

## Dependency Injection Support

The logging system can be integrated with Microsoft's dependency injection container. See `Examples.cs` for detailed integration examples.

## Performance Considerations

- Use `UseBackgroundWorker = true` for high-volume logging
- Consider using `Size` rotation for unpredictable log volumes
- For resource-constrained environments, set appropriate `MaxTotalSizeBytes`
- Monitor the log directory size when using extensive archiving

## Implementation Details

The logging system consists of the following key components:

- `IAgctorLogger`: Interface for logging
- `LoggerFactory`: Central factory for creating loggers
- `FileLogger`: File-based logger with rotation support
- `ConsoleLogger`: Console-based logger
- `FileLoggerOptions`: Configuration for file logging
- Various supporting classes and enums

For more examples, see the `Examples.cs` file. 