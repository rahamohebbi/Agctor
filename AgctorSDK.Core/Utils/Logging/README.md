# Error Handling and Logging in Agctor SDK

This document describes the error handling and logging functionality implemented in the Agctor SDK.

## Logging

The logging system provides a consistent way to log messages across the Agctor SDK components. It is designed to be extensible and configurable.

### Components

- `IAgctorLogger`: Interface that defines the logging API with methods for different log levels (Trace, Debug, Info, Warning, Error, Critical).
- `ConsoleLogger`: Implementation that logs messages to the console with color-coding based on log level.
- `LoggerFactory`: Factory for creating logger instances with specified categories and log levels.

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
```

## Error Handling

The error handling system provides a centralized way to handle errors across the Agctor SDK components. It includes middleware for processing errors through a pipeline of handlers.

### Components

- `ErrorHandlingMiddleware`: Middleware that processes errors through a pipeline of handlers.
- `ErrorContext`: Context information for error handling, including the exception, source, and original message.
- `ErrorHandlingDelegate`: Delegate type for error handling functions.

### Usage

```csharp
// Create an error handler
var errorHandler = new ErrorHandlingMiddleware(logger);

// Add custom error handlers
errorHandler.UseHandler(async (context) => {
    // Custom error handling logic
    logger.Error(context.Exception, "Custom error handler");
    
    // Mark as handled if appropriate
    context.IsHandled = true;
    return Task.CompletedTask;
});

// Handle an error
var errorContext = await errorHandler.HandleErrorAsync(exception, "SourceComponent", originalMessage);

// Check if the error was handled
if (errorContext.IsHandled) {
    // Error was handled by one of the handlers
}
```

### Creating Error Responses

The middleware provides a utility method for creating standardized error response messages:

```csharp
// Create a standard error response
var errorResponse = ErrorHandlingMiddleware.CreateErrorResponse(
    exception, 
    "SourceComponent", 
    originalMessage);
```

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