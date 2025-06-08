using System;
using System.Threading.Tasks;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Interfaces;
using System.Collections.Generic;

namespace AgctorSDK.Core.Utils.ErrorHandling
{
    /// <summary>
    /// Delegate for error handling middleware.
    /// </summary>
    /// <param name="context">The error context</param>
    /// <returns>Task representing the asynchronous operation</returns>
    public delegate Task ErrorHandlingDelegate(ErrorContext context);
    
    /// <summary>
    /// Context information for error handling.
    /// </summary>
    public class ErrorContext
    {
        /// <summary>
        /// Gets or sets the exception that occurred.
        /// </summary>
        public Exception Exception { get; set; }
        
        /// <summary>
        /// Gets or sets the source of the error (e.g., component name).
        /// </summary>
        public string Source { get; set; }
        
        /// <summary>
        /// Gets or sets the original message envelope, if available.
        /// </summary>
        public IMessageEnvelope? OriginalMessage { get; set; }
        
        /// <summary>
        /// Gets or sets additional properties relevant to the error.
        /// </summary>
        public Dictionary<string, object> Properties { get; } = new Dictionary<string, object>();
        
        /// <summary>
        /// Gets or sets whether the error has been handled.
        /// </summary>
        public bool IsHandled { get; set; }
        
        /// <summary>
        /// Gets or sets the result of error handling, if any.
        /// </summary>
        public object? Result { get; set; }
        
        /// <summary>
        /// Initializes a new instance of the ErrorContext class.
        /// </summary>
        /// <param name="exception">The exception that occurred</param>
        /// <param name="source">The source of the error</param>
        /// <param name="originalMessage">The original message, if available</param>
        public ErrorContext(Exception exception, string source, IMessageEnvelope? originalMessage = null)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            OriginalMessage = originalMessage;
        }
    }
    
    /// <summary>
    /// Middleware for centralized error handling.
    /// Provides a pipeline for handling errors with multiple handlers.
    /// </summary>
    public class ErrorHandlingMiddleware
    {
        private readonly IAgctorLogger _logger;
        private readonly List<ErrorHandlingDelegate> _handlers = new List<ErrorHandlingDelegate>();
        
        /// <summary>
        /// Initializes a new instance of the ErrorHandlingMiddleware class.
        /// </summary>
        /// <param name="logger">The logger to use</param>
        public ErrorHandlingMiddleware(IAgctorLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Add default handlers
            UseHandler(LogErrorHandler);
        }
        
        /// <summary>
        /// Adds a new error handling delegate to the pipeline.
        /// </summary>
        /// <param name="handler">The handler to add</param>
        /// <returns>This instance for method chaining</returns>
        public ErrorHandlingMiddleware UseHandler(ErrorHandlingDelegate handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            
            _handlers.Add(handler);
            return this;
        }
        
        /// <summary>
        /// Handles an error through the middleware pipeline.
        /// </summary>
        /// <param name="exception">The exception to handle</param>
        /// <param name="source">The source of the error</param>
        /// <param name="originalMessage">The original message, if available</param>
        /// <returns>The error context with handling results</returns>
        public async Task<ErrorContext> HandleErrorAsync(Exception exception, string source, IMessageEnvelope? originalMessage = null)
        {
            var context = new ErrorContext(exception, source, originalMessage);
            
            foreach (var handler in _handlers)
            {
                try
                {
                    await handler(context);
                    
                    if (context.IsHandled)
                    {
                        break;
                    }
                }
                catch (Exception handlerException)
                {
                    // Log error in handler but continue pipeline
                    _logger.Error(handlerException, "Error in error handler: {0}", handlerException.Message);
                }
            }
            
            return context;
        }
        
        /// <summary>
        /// Default handler for logging errors.
        /// </summary>
        private Task LogErrorHandler(ErrorContext context)
        {
            var messageId = context.OriginalMessage?.Id ?? "N/A";
            var messageType = context.OriginalMessage?.Payload?.GetType().Name ?? "Unknown";
            
            _logger.Error(context.Exception, 
                "Error in {0}: {1}. MessageId: {2}, MessageType: {3}", 
                context.Source,
                context.Exception.Message,
                messageId,
                messageType);
            
            // Don't mark as handled - allow other handlers to process
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Creates an error handler for specific exception types.
        /// </summary>
        /// <typeparam name="TException">The type of exception to handle</typeparam>
        /// <param name="handler">The handler function</param>
        /// <returns>This instance for method chaining</returns>
        public ErrorHandlingMiddleware UseExceptionHandler<TException>(Func<TException, ErrorContext, Task> handler) where TException : Exception
        {
            return UseHandler(async context =>
            {
                if (context.Exception is TException typedException)
                {
                    await handler(typedException, context);
                }
            });
        }
        
        /// <summary>
        /// Creates a standard error response message envelope for an exception.
        /// </summary>
        /// <param name="exception">The exception to create a response for</param>
        /// <param name="source">The source of the error (e.g., actor ID)</param>
        /// <param name="originalMessage">The original message, if available</param>
        /// <returns>A message envelope containing the error response</returns>
        public static IMessageEnvelope CreateErrorResponse(Exception exception, string source, IMessageEnvelope? originalMessage = null)
        {
            string originalSenderId = "unknown";
            string originalMessageId = "unknown";
            
            if (originalMessage?.Headers != null)
            {
                if (originalMessage.Headers.TryGetValue("SenderId", out var sid))
                {
                    originalSenderId = sid;
                }
                
                originalMessageId = originalMessage.Id ?? "unknown";
            }
            
            var errorPayload = $"Error: {exception.Message}";
            
            var errorMetadata = new Dictionary<string, object> 
            { 
                ["Timestamp"] = DateTimeOffset.UtcNow,
                ["ExceptionType"] = exception.GetType().Name
            };
            
            if (originalMessage?.Metadata != null && 
                originalMessage.Metadata.TryGetValue("CorrelationId", out var corrId))
            {
                errorMetadata["CorrelationId"] = corrId;
            }
            
            var errorHeaders = new Dictionary<string, string>
            {
                ["SenderId"] = source,
                ["ReceiverId"] = originalSenderId,
                ["MessageType"] = "ErrorResponse",
                ["OriginalMessageId"] = originalMessageId
            };
            
            return new MessageEnvelope(
                payload: errorPayload,
                metadata: errorMetadata,
                id: Guid.NewGuid().ToString(),
                headers: errorHeaders
            );
        }
    }
} 