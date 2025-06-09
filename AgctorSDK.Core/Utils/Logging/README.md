# Error Handling and Logging in Agctor SDK

This document describes the error handling and logging functionality implemented in the Agctor SDK.

## Logging

The logging system provides a consistent way to log messages across the Agctor SDK components. It is designed to be extensible and configurable.

### Components

- `IAgctorLogger`: Interface that defines the logging API with methods for different log levels (Trace, Debug, Info, Warning, Error, Critical).
- `ConsoleLogger`: Implementation that logs messages to the console with color-coding based on log level.
- `FileLogger`: Implementation that logs messages to files with configurable rotation and cleanup.
- `LoggerFactory`: Factory for creating logger instances with specified categories and log levels.
- `ILoggerProvider`: Interface for logger providers to support extensibility.

### Usage

```csharp
// Get a logger instance for a specific category
var logger = LoggerFactory.CreateLogger("MyComponent");

// Log messages at different levels
logger.Trace("Detailed trace information");
logger.Debug("Debug information");
logger.Info("General information");
logger.Warning("Warning message");
logger.Error("Error message");
logger.Error(exception, "Error with exception");
logger.Critical("Critical error message");
logger.Critical(exception, "Critical error with exception");
```

### Configuration

Global log level and other settings can be configured through the `LoggerFactory`:

```csharp
// Set minimum log level for all loggers
LoggerFactory.SetDefaultMinLevel(LogLevel.Debug);

// Configure timestamp inclusion
LoggerFactory.SetIncludeTimestamps(true);

// Add a file logger
LoggerFactory.AddFileLogger(new FileLoggerOptions
{
    LogDirectory = "logs",
    FileName = "agctor-{date}.log",
    RotationStrategy = RotationStrategy.Daily,
    MaxDaysToKeep = 30,
    MaxLogFiles = 50
});

// Use multiple loggers simultaneously
var consoleProvider = new ConsoleLoggerProvider(LogLevel.Info, true);
var fileProvider = new FileLoggerProvider(new FileLoggerOptions
{
    LogDirectory = "logs",
    FileName = "errors-{date}.log",
    RotationStrategy = RotationStrategy.Size,
    MaxFileSizeBytes = 5 * 1024 * 1024 // 5MB
}, LogLevel.Error); // Only log errors and above to this file

LoggerFactory.AddProvider(consoleProvider);
LoggerFactory.AddProvider(fileProvider);

// Messages will be sent to all configured providers
var logger = LoggerFactory.CreateLogger("MyComponent");
logger.Info("This goes to console only");
logger.Error("This goes to both console and file");
```

### File Logger Options

The `FileLogger` supports various configuration options:

- **Log Directory**: Where log files are stored
- **Filename Pattern**: Supports `{date}` and `{category}` placeholders
- **Rotation Strategy**: None, Size, Daily, or Hourly
- **Size Limits**: Maximum file size (for size-based rotation)
- **Retention Policy**: Maximum days to keep logs and maximum number of log files

Example:

```csharp
var options = new FileLoggerOptions
{
    LogDirectory = "logs/system",
    FileName = "{category}-{date}.log",
    UseTimestampInFilename = true,
    RotationStrategy = RotationStrategy.Daily,
    MaxDaysToKeep = 90,
    MaxLogFiles = 100
};

LoggerFactory.AddFileLogger(options);
```

## Error Handling

The error handling system provides a centralized way to handle errors across the Agctor SDK components. It includes middleware for processing errors through a pipeline of handlers.

### Components

- `ErrorHandlingMiddleware`: Middleware that processes errors through a pipeline of handlers.
- `ErrorContext`: Context information for error handling, including the exception, source, and original message.
- `ErrorHandlingDelegate`: Delegate type for error handling functions.

### Usage

```csharp
// Create error handling middleware
var errorHandler = new ErrorHandlingMiddleware(logger);

// Add custom error handlers
errorHandler.Use((context, next) =>
{
    // Log all errors
    logger.Error(context.Exception, $"Error in {context.Source}: {context.Exception.Message}");
    
    // Continue to next handler
    return next(context);
});

// Add specialized handlers
errorHandler.Use((context, next) =>
{
    // Handle specific exception types
    if (context.Exception is OperationCanceledException)
    {
        logger.Info($"Operation cancelled: {context.Source}");
        return Task.CompletedTask;
    }
    
    // Continue to next handler
    return next(context);
});

// Handle an error
await errorHandler.HandleErrorAsync(new ErrorContext
{
    Exception = exception,
    Source = "MyComponent",
    Message = "Failed to process request"
});
```

## Extensibility

The logging and error handling systems are designed to be extensible:

- Implement `IAgctorLogger` to create custom loggers
- Implement `ILoggerProvider` to create custom logger providers
- Add custom error handlers to the middleware pipeline

This enables integration with external logging frameworks and custom error handling logic.

## Integration with Dependency Injection

Both logging and error handling components are integrated with the dependency injection system:

```csharp
// Register logging and error handling services
services.AddSingleton<IAgctorLogger>(sp => 
{
    var options = sp.GetService<IOptions<AgctorOptions>>()?.Value;
    var minLevel = options?.EnableDetailedLogging == true ? LogLevel.Trace : LogLevel.Info;
    return LoggerFactory.CreateLogger("Agctor", minLevel);
});

services.AddSingleton<ErrorHandlingMiddleware>();
```

## Best Practices

1. **Use appropriate log levels**: Use Trace and Debug for detailed information, Info for general progress, Warning for potential issues, Error for actual errors, and Critical for system-threatening issues.

2. **Include context in error handling**: Always provide the source component and relevant message context when handling errors.

3. **Create specific error responses**: When responding to errors, include relevant information to help diagnose and resolve the issue.

4. **Log exceptions with stack traces**: When logging exceptions, include the exception object to capture stack traces and inner exceptions.

5. **Handle errors at appropriate levels**: Handle errors at the level where they can be properly addressed, escalating only when necessary. 