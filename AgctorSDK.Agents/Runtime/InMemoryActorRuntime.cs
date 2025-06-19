using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.ErrorHandling;

namespace AgctorSDK.Core.Runtime
{
    /// <summary>
    /// In-memory actor runtime implementation that provides basic actor lifecycle management,
    /// message dispatch, and per-actor message queues. This is the MVP backend for the Agctor SDK.
    /// </summary>
    public class InMemoryActorRuntime : IActorRuntimeAdapter
    {
        private readonly ConcurrentDictionary<string, ActorInstance> _actors = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<IMessageEnvelope>> _pendingRequests = new();
        private readonly Dictionary<string, object> _configuration = new();
        private readonly object _lockObject = new();
        private readonly CancellationTokenSource _shutdownTokenSource = new();
        private readonly IAgctorLogger _logger;
        private readonly ErrorHandlingMiddleware _errorHandler;
        
        public InMemoryActorRuntime()
        {
            _logger = Utils.Logging.LoggerFactory.CreateLogger("InMemoryActorRuntime");
            _errorHandler = new ErrorHandlingMiddleware(_logger);
        }
        
        public InMemoryActorRuntime(IAgctorLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _errorHandler = new ErrorHandlingMiddleware(_logger);
        }
        
        private bool _isInitialized;
        private bool _isDisposed;
        private DateTimeOffset _startTime;
        private long _totalMessagesProcessed;

        public string Name => "InMemoryActorRuntime";
        public string Version => "1.0.0";
        public bool IsInitialized => _isInitialized;
        public IReadOnlyDictionary<string, object> Configuration => _configuration;

        public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;
        public event EventHandler<ActorStoppedEventArgs>? ActorStopped;
        public event EventHandler<MessageSentEventArgs>? MessageSent;

        /// <summary>
        /// Represents an actor instance with its message queue and processing task.
        /// </summary>
        private class ActorInstance
        {
            public IActor Actor { get; }
            public Channel<IMessageEnvelope> MessageQueue { get; }
            public Task ProcessingTask { get; }
            public CancellationTokenSource CancellationTokenSource { get; }
            public DateTimeOffset CreatedAt { get; }

            public ActorInstance(IActor actor, Channel<IMessageEnvelope> messageQueue, 
                Task processingTask, CancellationTokenSource cancellationTokenSource)
            {
                Actor = actor;
                MessageQueue = messageQueue;
                ProcessingTask = processingTask;
                CancellationTokenSource = cancellationTokenSource;
                CreatedAt = DateTimeOffset.UtcNow;
            }
        }

        public Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (_isInitialized)
            {
                LogTrace("Runtime already initialized");
                return Task.CompletedTask;
            }

            LogTrace("Initializing InMemoryActorRuntime...");

            // Store configuration
            lock (_lockObject)
            {
                _configuration.Clear();
                foreach (var kvp in configuration)
                {
                    _configuration[kvp.Key] = kvp.Value;
                }
            }

            _startTime = DateTimeOffset.UtcNow;
            _isInitialized = true;

            LogTrace($"InMemoryActorRuntime initialized successfully at {_startTime}");
            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!_isInitialized)
            {
                LogTrace("Runtime not initialized, nothing to shutdown");
                return;
            }

            LogTrace("Shutting down InMemoryActorRuntime...");

            // Signal shutdown to all actors
            _shutdownTokenSource.Cancel();

            // Stop all actors gracefully
            var actorIds = _actors.Keys.ToList();
            var stopTasks = actorIds.Select(actorId => StopActorAsync(actorId, cancellationToken)).ToArray();
            
            try
            {
                await Task.WhenAll(stopTasks);
            }
            catch (Exception ex)
            {
                LogTrace($"Exception during actor shutdown: {ex.Message}");
                // Potentially log more details or handle specific exceptions
            }
            
            // Clear pending requests
            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled();
            }
            _pendingRequests.Clear();

            _isInitialized = false;
            LogTrace("InMemoryActorRuntime shutdown completed");
        }

        public Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, IActor
        {
            return SpawnActorAsync(actorId, (id) => CreateActorInstance<T>(id), initializationData, cancellationToken);
        }

        public async Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null,
            CancellationToken cancellationToken = default) where T : class, IActor
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (string.IsNullOrEmpty(actorId))
            {
                throw new ArgumentException("Actor ID cannot be null or empty.", nameof(actorId));
            }

            var newActorInstance = actorFactory(actorId);

            // Setup agent-specific properties before initialization
            if (newActorInstance is IAgent agent && initializationData is AgentInitializationData agentInitData)
            {
                if (agentInitData.AgentFactory == null)
                {
                    throw new InvalidOperationException("AgentFactory must be provided in AgentInitializationData for agent actors.");
                }
                // The 'as' operator is safer than a direct cast if Agent evolves
                if (agent is Agent baseAgent) 
                {
                    baseAgent.SetAgentFactory(agentInitData.AgentFactory);
                    if(!string.IsNullOrEmpty(agentInitData.ParentAgentId))
                    {
                        baseAgent.SetParentAgentId(agentInitData.ParentAgentId);
                    }
                }
            }
            
            await newActorInstance.InitializeAsync(cancellationToken);
            
            if (newActorInstance.State == ActorState.Faulted)
            {
                 throw new InvalidOperationException($"Actor '{actorId}' faulted during initialization.");
            }

            var messageQueue = Channel.CreateUnbounded<IMessageEnvelope>(new UnboundedChannelOptions
            {
                SingleReader = true, // Each actor has its own processing loop
                SingleWriter = false // Multiple senders can write to the queue
            });
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownTokenSource.Token, cancellationToken);
            var processingTask = ProcessActorMessagesAsync(newActorInstance, messageQueue.Reader, cts.Token);

            var actorInstanceContainer = new ActorInstance(newActorInstance, messageQueue, processingTask, cts);

            if (!_actors.TryAdd(actorId, actorInstanceContainer))
            {
                // Actor might have been added by another thread, or Stop has been called.
                // Clean up resources for this attempt.
                cts.Cancel(); 
                await processingTask.ContinueWith(_ => { /* Observe task completion/exceptions */ }, TaskScheduler.Default);
                newActorInstance.ShutdownAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult(); // Best effort
                throw new InvalidOperationException($"Failed to add actor '{actorId}' to collection. It might already exist or runtime is shutting down.");
            }
            
            LogTrace($"Spawned actor '{actorId}' of type '{typeof(T).Name}'");
            ActorSpawned?.Invoke(this, new ActorSpawnedEventArgs(actorId, typeof(T).Name));
            return newActorInstance;
        }

        public async Task RegisterActorAsync(IActor actor, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            string actorId = actor.Id;
            
            if (string.IsNullOrEmpty(actorId))
            {
                throw new ArgumentException("Actor ID cannot be null or empty.", nameof(actor));
            }

            var messageQueue = Channel.CreateUnbounded<IMessageEnvelope>(new UnboundedChannelOptions
            {
                SingleReader = true, // Each actor has its own processing loop
                SingleWriter = false // Multiple senders can write to the queue
            });
            
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownTokenSource.Token, cancellationToken);
            var processingTask = ProcessActorMessagesAsync(actor, messageQueue.Reader, cts.Token);

            var actorInstanceContainer = new ActorInstance(actor, messageQueue, processingTask, cts);

            if (!_actors.TryAdd(actorId, actorInstanceContainer))
            {
                // Actor might have been added by another thread, or Stop has been called.
                // Clean up resources for this attempt.
                cts.Cancel(); 
                await processingTask.ContinueWith(_ => { /* Observe task completion/exceptions */ }, TaskScheduler.Default);
                actor.ShutdownAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult(); // Best effort
                throw new InvalidOperationException($"Failed to add actor '{actorId}' to collection. It might already exist or runtime is shutting down.");
            }
            
            LogTrace($"Registered actor '{actorId}' of type '{actor.GetType().Name}'");
            ActorSpawned?.Invoke(this, new ActorSpawnedEventArgs(actorId, actor.GetType().Name));
        }

        public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
        {
            ThrowIfDisposed();
            if (_actors.TryGetValue(actorId, out var actorInstance))
            {
                if (actorInstance.Actor is T typedActor)
                {
                    return Task.FromResult<T?>(typedActor);
                }
                // Actor found but type mismatch
                LogTrace($"Actor '{actorId}' found but type mismatch. Expected: {typeof(T).Name}, Actual: {actorInstance.Actor.GetType().Name}");
                return Task.FromResult<T?>(null);
            }
            return Task.FromResult<T?>(null);
        }

        public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, 
            IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (!_actors.TryGetValue(targetActorId, out var actorInstance))
            {
                // Optionally, could dead-letter or log with higher severity
                LogTrace($"Target actor '{targetActorId}' not found for SendMessageAsync. Message of type '{message?.GetType().Name ?? "null"}' will be dropped.");
                return Task.CompletedTask; // Or throw new InvalidOperationException($"Target actor '{targetActorId}' not found");
            }

            var messageId = Guid.NewGuid().ToString();
            
            var mcpHeaders = headers != null ? new Dictionary<string, string>(headers) : new Dictionary<string, string>();
            mcpHeaders["SenderId"] = senderId ?? "system"; // Per MCP, use originating agent or "system"
            mcpHeaders["ReceiverId"] = targetActorId;    // Per MCP
            if (!mcpHeaders.ContainsKey("MessageType"))
            {
                mcpHeaders["MessageType"] = message is string ? "Prompt" : (message?.GetType().Name ?? "Unknown");
            }
            mcpHeaders["Version"] = "1.0"; // Example default version

            var mcpMetadata = new Dictionary<string, object>
            {
                ["Timestamp"] = DateTimeOffset.UtcNow // Per MCP, timestamp of creation/dispatch
                // Other relevant metadata like "Priority" could be added if sourced from somewhere
            };

            var envelope = new AgctorSDK.Core.Messages.MessageEnvelope(
                id: messageId,
                payload: message ?? new object(), // Ensure payload is not null
                metadata: mcpMetadata,
                headers: mcpHeaders
            );

            LogTrace($"Attempting to send message '{messageId}' from '{senderId ?? "system"}' to '{targetActorId}' (Type: {mcpHeaders["MessageType"]})");

            if (!actorInstance.MessageQueue.Writer.TryWrite(envelope))
            {
                LogTrace($"Failed to enqueue message to actor '{targetActorId}'. Queue might be full or closed.");
                // Depending on policy, could throw or just log
                // throw new InvalidOperationException($"Failed to enqueue message to actor '{targetActorId}'");
            }
            else
            {
                Interlocked.Increment(ref _totalMessagesProcessed);
                MessageSent?.Invoke(this, new MessageSentEventArgs(messageId, senderId, targetActorId, mcpHeaders["MessageType"]));
                LogTrace($"Successfully enqueued message '{messageId}' to '{targetActorId}'");
            }
            
            return Task.CompletedTask;
        }

        public async Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout,
            string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
            where TResponse : class
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (!_actors.TryGetValue(targetActorId, out var actorInstance))
            {
                throw new InvalidOperationException($"Target actor '{targetActorId}' not found for SendMessageAsync<TResponse>.");
            }

            var messageId = Guid.NewGuid().ToString();
            var correlationId = Guid.NewGuid().ToString(); // Unique ID for this request-response pair

            var tcs = new TaskCompletionSource<IMessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(correlationId, tcs))
            {
                // This should be rare if correlationIds are unique GUIDs
                throw new InvalidOperationException($"Failed to register pending request for correlationId '{correlationId}'.");
            }

            var mcpHeaders = headers != null ? new Dictionary<string, string>(headers) : new Dictionary<string, string>();
            mcpHeaders["SenderId"] = senderId ?? "system";
            mcpHeaders["ReceiverId"] = targetActorId;
            if (!mcpHeaders.ContainsKey("MessageType"))
            {
                mcpHeaders["MessageType"] = message is string ? "Prompt" : (message?.GetType().Name ?? "Unknown");
            }
            mcpHeaders["Version"] = "1.0";
            // mcpHeaders["ReplyTo"] could be added if a specific reply path is known, e.g. runtime's own address.
            // For now, relies on correlationId.

            var mcpMetadata = new Dictionary<string, object>
            {
                ["Timestamp"] = DateTimeOffset.UtcNow,
                ["CorrelationId"] = correlationId // Key for matching response
            };

            var envelope = new AgctorSDK.Core.Messages.MessageEnvelope(
                id: messageId,
                payload: message ?? new object(),
                metadata: mcpMetadata,
                headers: mcpHeaders
            );
            
            LogTrace($"Attempting to send request-response message '{messageId}' (CorrId: {correlationId}) from '{senderId ?? "system"}' to '{targetActorId}' (Type: {mcpHeaders["MessageType"]})");

            if (!actorInstance.MessageQueue.Writer.TryWrite(envelope))
            {
                _pendingRequests.TryRemove(correlationId, out _); // Clean up TCS
                throw new InvalidOperationException($"Failed to enqueue request message to actor '{targetActorId}'.");
            }
            
            Interlocked.Increment(ref _totalMessagesProcessed);
            MessageSent?.Invoke(this, new MessageSentEventArgs(messageId, senderId, targetActorId, mcpHeaders["MessageType"]));
            LogTrace($"Successfully enqueued request-response message '{messageId}' to '{targetActorId}'");

            // Await response with timeout
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownTokenSource.Token);
            if (timeout > TimeSpan.Zero)
            {
                var delayTask = Task.Delay(timeout, linkedCts.Token);
                var completedTask = await Task.WhenAny(tcs.Task, delayTask);

                if (completedTask == delayTask)
                {
                    _pendingRequests.TryRemove(correlationId, out _);
                    tcs.TrySetCanceled(); // Attempt to cancel to prevent resource leaks if not already completed
                    throw new TimeoutException($"Request to actor '{targetActorId}' with correlation ID '{correlationId}' timed out after {timeout.TotalMilliseconds}ms.");
                }
            }
            else // No timeout, wait indefinitely or until cancellation
            {
                await tcs.Task.WaitAsync(linkedCts.Token); // Use WaitAsync for better cancellation behavior
            }
            
            _pendingRequests.TryRemove(correlationId, out _); // Clean up TCS once done

            var responseEnvelope = await tcs.Task; // Get the result (or rethrow exception if set)

            if (responseEnvelope.Payload is TResponse typedResponse)
            {
                return typedResponse;
            }
            
            // If the payload is not TResponse, it's an unexpected response type or an error object.
            // The IActor.ReceiveAsync is expected to return IMessageEnvelope, with the actual result in its Payload.
            // Or it might return an envelope indicating an error.
            var errorPayload = responseEnvelope.Payload as Exception; // Check if payload is an exception
            if (errorPayload != null)
            {
                throw new InvalidOperationException($"Actor '{targetActorId}' responded with an error for correlation ID '{correlationId}': {errorPayload.Message}", errorPayload);
            }
            
            throw new InvalidOperationException(
                $"Actor '{targetActorId}' responded with an incompatible payload type for correlation ID '{correlationId}'. " +
                $"Expected '{typeof(TResponse).FullName}', but received '{responseEnvelope.Payload?.GetType().FullName ?? "null"}'.");
        }

        public async Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (!_actors.TryRemove(actorId, out var actorInstance))
            {
                LogTrace($"Actor '{actorId}' not found or already removed during StopActorAsync");
                return; // Actor not found or already removed
            }

            LogTrace($"Stopping actor '{actorId}'...");

            try
            {
                // Signal the actor's processing loop to cancel
                actorInstance.CancellationTokenSource.Cancel();
                
                // Complete the message queue to unblock the reader
                actorInstance.MessageQueue.Writer.TryComplete();

                // Give actor a chance to shutdown gracefully
                await actorInstance.Actor.ShutdownAsync(cancellationToken).ConfigureAwait(false);
                
                // Wait for the processing task to complete
                // Add a timeout to prevent hanging indefinitely if ShutdownAsync or processing loop doesn't finish
                var processingCompletionTask = actorInstance.ProcessingTask;
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // 5-second timeout for processing loop
                
                await Task.WhenAny(processingCompletionTask, timeoutTask).ConfigureAwait(false);

                if (!processingCompletionTask.IsCompleted)
                {
                    LogTrace($"Actor '{actorId}' processing task did not complete within timeout during shutdown.");
                }
                else if (processingCompletionTask.IsFaulted)
                {
                    LogTrace($"Actor '{actorId}' processing task faulted during shutdown: {processingCompletionTask.Exception?.GetBaseException().Message}");
                }
            }
            catch (OperationCanceledException)
            {
                LogTrace($"Stopping actor '{actorId}' was canceled.");
                // Expected if cancellationToken is triggered
            }
            catch (Exception ex)
            {
                // Log error during actor shutdown
                LogTrace($"Error stopping actor '{actorId}': {ex.Message}");
                // Potentially rethrow or handle as critical error
            }
            finally
            {
                actorInstance.CancellationTokenSource.Dispose();
                // We rely on the actor's implementation of ShutdownAsync to correctly set its final state.
            }

            LogTrace($"Actor '{actorId}' stopped.");
            ActorStopped?.Invoke(this, new ActorStoppedEventArgs(actorId, actorInstance.Actor.ActorType, "Runtime requested stop"));
        }

        public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return Task.FromResult<IEnumerable<string>>(_actors.Keys.ToList()); // Return a copy
        }

        public Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var activeActorCount = _actors.Count;
            var uptime = DateTimeOffset.UtcNow - _startTime;
            var messagesPerSecond = uptime.TotalSeconds > 0 ? _totalMessagesProcessed / uptime.TotalSeconds : 0;

            // This is a placeholder; a real implementation would require tracking processing times
            var averageProcessingTime = 0.0;

            var stats = new RuntimeStatistics(
                activeActorCount: _actors.Count,
                totalMessagesProcessed: _totalMessagesProcessed,
                messagesPerSecond: messagesPerSecond,
                averageMessageProcessingTime: averageProcessingTime,
                uptime: uptime,
                memoryUsageBytes: GC.GetTotalMemory(false) // Get current process memory
            );
            return Task.FromResult<IRuntimeStatistics>(stats);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                // Shutdown if not already done
                if (_isInitialized)
                {
                    try 
                    {
                        // Use a short timeout for dispose to prevent blocking indefinitely
                        ShutdownAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch(Exception ex)
                    {
                        LogError(ex, "Exception during Dispose/Shutdown");
                        
                        // Use error handling middleware
                        _errorHandler.HandleErrorAsync(ex, "Runtime", null)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                }
                _shutdownTokenSource.Dispose();

                foreach (var actorInstance in _actors.Values)
                {
                    actorInstance.CancellationTokenSource.Dispose();
                    // Actor processing tasks should have been awaited in ShutdownAsync
                }
                _actors.Clear();
                _pendingRequests.Clear();
            }
            _isDisposed = true;
        }

        private T CreateActorInstance<T>(string actorId) where T : class, IActor
        {
            try
            {
                // First, try the constructor that takes a string 'id', which is the convention for Agents.
                return (T)Activator.CreateInstance(typeof(T), actorId)!;
            }
            catch (MissingMethodException inner)
            {
                throw new InvalidOperationException($"Could not create instance of actor type '{typeof(T).Name}'. It must have a public constructor that takes a string 'id'. See inner exception.", inner);
            }
        }
        
        /// <summary>
        /// Processes messages for a specific actor in a dedicated task.
        /// This implements the per-actor message queue and dispatch mechanism.
        /// </summary>
        private async Task ProcessActorMessagesAsync(IActor actor, ChannelReader<IMessageEnvelope> messageReader,
            CancellationToken cancellationToken)
        {
            string actorId = actor.Id;
            string actorType = actor.ActorType;
            LogTrace($"Started message processing loop for actor '{actorId}' (Type: {actorType})");
            
            int processedCount = 0;
            
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    IMessageEnvelope? envelope = null;
                    bool hasMessage = false;
                    
                    try
                    {
                        hasMessage = await messageReader.WaitToReadAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        LogTrace($"Message wait for actor '{actorId}' was canceled.");
                        break; // Exit loop on cancellation
                    }
                    catch (ChannelClosedException)
                    {
                        LogTrace($"Message channel for actor '{actorId}' was closed.");
                        break; // Exit loop if channel closed
                    }
                    
                    if (!hasMessage)
                    {
                        break; // Exit loop if no more messages available and channel closed
                    }
                    
                    try
                    {
                        if (!messageReader.TryRead(out envelope))
                        {
                            continue; // Skip if no message was read (potentially due to race condition)
                        }
                        
                        LogTrace($"Processing message ID '{envelope.Id}' for actor '{actorId}'. Headers: {string.Join(", ", envelope.Headers?.Select(h => $"{h.Key}={h.Value}") ?? Array.Empty<string>())}");
                        
                        string? senderId = null;
                        if (envelope.Headers != null && envelope.Headers.TryGetValue("SenderId", out var sid))
                        {
                            senderId = sid;
                        }
                        
                        string? correlationId = null;
                        if (envelope.Metadata != null && envelope.Metadata.TryGetValue("CorrelationId", out var cid))
                        {
                            correlationId = cid?.ToString();
                        }
                        
                        // Invoke the actor's message handler
                        IMessageEnvelope? responseEnvelope = null;
                        try
                        {
                            responseEnvelope = await actor.ReceiveAsync(envelope, cancellationToken);
                            LogTrace($"Actor '{actorId}' processed message '{envelope.Id}' successfully.");
                        }
                        catch (Exception ex)
                        {
                            // Use error handler middleware for centralized error handling
                            LogError(ex, $"Error in actor '{actorId}' processing message '{envelope.Id}'");
                            
                            // Create error context and process through middleware
                            var errorContext = await _errorHandler.HandleErrorAsync(ex, actorId, envelope);
                            
                            // Create or use error response envelope
                            if (errorContext.Result is IMessageEnvelope errorEnvelope)
                            {
                                responseEnvelope = errorEnvelope;
                            }
                            else
                            {
                                // Create standard error response
                                var errorHeaders = new Dictionary<string, string>
                                {
                                    ["SenderId"] = actorId,
                                    ["ReceiverId"] = senderId ?? "unknown",
                                    ["MessageType"] = "ErrorResponse",
                                    ["OriginalMessageId"] = envelope.Id ?? "unknown"
                                };
                                
                                var errorMetadata = new Dictionary<string, object>
                                {
                                    ["Timestamp"] = DateTimeOffset.UtcNow,
                                    ["ExceptionType"] = ex.GetType().Name
                                };
                                
                                if (!string.IsNullOrEmpty(correlationId))
                                {
                                    errorMetadata["CorrelationId"] = correlationId;
                                }
                                
                                responseEnvelope = new MessageEnvelope(
                                    payload: $"Error: {ex.Message}",
                                    metadata: errorMetadata,
                                    id: Guid.NewGuid().ToString(),
                                    headers: errorHeaders
                                );
                            }
                        }
                        
                        // Handle request-response pattern
                        if (!string.IsNullOrEmpty(correlationId) && _pendingRequests.TryGetValue(correlationId, out var tcs))
                        {
                            if (responseEnvelope != null)
                            {
                                LogTrace($"Setting response for correlation ID '{correlationId}' from actor '{actorId}'");
                                tcs.TrySetResult(responseEnvelope);
                            }
                            else
                            {
                                LogTrace($"No response envelope provided for correlation ID '{correlationId}' from actor '{actorId}'");
                                var noResponseErrorEnvelope = new MessageEnvelope(
                                    payload: new InvalidOperationException($"Actor '{actorId}' did not provide a response"),
                                    metadata: new Dictionary<string, object> { ["CorrelationId"] = correlationId, ["Timestamp"] = DateTimeOffset.UtcNow },
                                    id: Guid.NewGuid().ToString(),
                                    headers: new Dictionary<string, string>
                                    {
                                        ["SenderId"] = actorId,
                                        ["ReceiverId"] = senderId ?? "unknown",
                                        ["MessageType"] = "ErrorResponse",
                                        ["OriginalMessageId"] = envelope.Id ?? "unknown"
                                    }
                                );
                                tcs.TrySetResult(noResponseErrorEnvelope);
                            }
                        }
                        
                        processedCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        LogTrace($"Message processing for actor '{actorId}' was canceled during processing.");
                        break; // Exit loop on cancellation
                    }
                    catch (Exception ex)
                    {
                        // Log and handle unexpected errors in message processing
                        LogError(ex, $"Unexpected error processing message for actor '{actorId}'");
                        
                        // Use error handling middleware
                        await _errorHandler.HandleErrorAsync(ex, $"Runtime:{actorId}", envelope);
                        
                        // Continue loop to process other messages in the queue
                    }
                }
            }
            catch (Exception ex)
            {
                // Log fatal errors in the actor's message loop
                LogError(ex, $"Fatal error in message processing loop for actor '{actorId}'");
                
                // Use error handling middleware for fatal errors
                await _errorHandler.HandleErrorAsync(ex, $"Runtime:{actorId}", null);
                
                // Actor state should be marked as faulted
                if (actor is Agent baseAgent)
                {
                    try
                    {
                        // Try to change actor state to faulted
                        typeof(Agent).GetMethod("ChangeActorState", 
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            ?.Invoke(baseAgent, new object[] { ActorState.Faulted, $"Fatal error: {ex.Message}" });
                    }
                    catch
                    {
                        // Ignore errors in reflection-based state change
                    }
                }
            }
            finally
            {
                LogTrace($"Message processing loop for actor '{actorId}' terminated. Processed {processedCount} messages.");
            }
        }
        
        // This method is from prd-cli-001.md for Human Agent Fallback
        // It's not directly related to MCP changes but is part of the existing file.
        public Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            LogTrace($"Human input requested by agent '{requestingAgentId}'. Prompt: '{prompt}'");

            // In a real CLI or UI application, this would involve presenting the prompt to the user
            // and waiting for their input. For this in-memory runtime, we might simulate or
            // require a host application to provide an implementation.

            // Placeholder: Simulate by logging and returning a predefined response or throwing NotImplemented.
            // Console.WriteLine($"AGENT ({requestingAgentId}) REQUESTS HUMAN INPUT:");
            // Console.WriteLine($"PROMPT: {prompt}");
            // Console.WriteLine($"INSTRUCTIONS: {instructions}");
            // Console.Write("Your input: ");
            // string humanInput = Console.ReadLine() ?? "";
            // return Task.FromResult(humanInput);

            // For a library, it's better to indicate it's not supported by this specific adapter directly,
            // or to have a mechanism to plug in a human input provider.
            var tcs = new TaskCompletionSource<string>();
            
            // This basic InMemory runtime doesn't have direct console access.
            // This should ideally be handled by a dedicated "HumanInteractionService" or similar
            // that the runtime can call, and that service would be implemented by the host application.
            LogTrace("InMemoryActorRuntime.RequestHumanInputAsync: Standard implementation relies on host providing UI/CLI interaction. Returning NotImplementedException for now.");
            tcs.SetException(new NotImplementedException("Human input handling is not directly implemented by InMemoryActorRuntime. A host-provided interaction mechanism is required."));
            
            return tcs.Task;
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        private void ThrowIfNotInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Actor runtime is not initialized.");
            }
        }
        
        // Use our logger for all logging
        private void LogTrace(string message)
        {
            _logger.Trace(message);
            
            // Also log to TestContext if available (for integration tests)
            try
            {
                var testContextType = Type.GetType("AgctorSDK.Core.IntegrationTests.TestHelpers.TestDependencies, AgctorSDK.Core.IntegrationTests");
                if (testContextType != null)
                {
                    var testContextProperty = testContextType.GetProperty("TestContext");
                    if (testContextProperty != null)
                    {
                        var testContext = testContextProperty.GetValue(null);
                        if (testContext != null)
                        {
                            var writeLineMethod = testContext.GetType().GetMethod("WriteLine", new[] { typeof(string) });
                            if (writeLineMethod != null)
                            {
                                writeLineMethod.Invoke(testContext, new[] { $"[RUNTIME] {message}" });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore any errors in test logging
            }
        }
        
        private void LogInfo(string message)
        {
            _logger.Info(message);
        }
        
        private void LogWarning(string message)
        {
            _logger.Warning(message);
        }
        
        private void LogError(string message)
        {
            _logger.Error(message);
        }
        
        private void LogError(Exception ex, string message)
        {
            _logger.Error(ex, message);
        }
    }

    // Internal statistics class
    internal class RuntimeStatistics : IRuntimeStatistics
    {
        public int ActiveActorCount { get; }
        public long TotalMessagesProcessed { get; }
        public double MessagesPerSecond { get; }
        public double AverageMessageProcessingTime { get; } // In milliseconds
        public TimeSpan Uptime { get; }
        public long MemoryUsageBytes { get; } // Process-wide, not specific to this runtime if in-proc with other things
        public IReadOnlyDictionary<string, object> AdditionalMetrics { get; }

        public RuntimeStatistics(int activeActorCount, long totalMessagesProcessed, double messagesPerSecond,
            double averageMessageProcessingTime, TimeSpan uptime, long memoryUsageBytes,
            IReadOnlyDictionary<string, object>? additionalMetrics = null)
        {
            ActiveActorCount = activeActorCount;
            TotalMessagesProcessed = totalMessagesProcessed;
            MessagesPerSecond = messagesPerSecond;
            AverageMessageProcessingTime = averageMessageProcessingTime;
            Uptime = uptime;
            MemoryUsageBytes = memoryUsageBytes;
            AdditionalMetrics = additionalMetrics ?? new Dictionary<string, object>();
        }
    }
} 