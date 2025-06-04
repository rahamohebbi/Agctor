using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Runtime
{
    /// <summary>
    /// In-memory actor runtime implementation that provides basic actor lifecycle management,
    /// message dispatch, and per-actor message queues. This is the MVP backend for the Agctor SDK.
    /// </summary>
    public class InMemoryActorRuntime : IActorRuntimeAdapter
    {
        private readonly ConcurrentDictionary<string, ActorInstance> _actors = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _pendingRequests = new();
        private readonly Dictionary<string, object> _configuration = new();
        private readonly object _lockObject = new();
        private readonly CancellationTokenSource _shutdownTokenSource = new();
        
        private bool _isInitialized;
        private bool _isDisposed;
        private DateTimeOffset _startTime;
        private long _totalMessagesProcessed;
        private long _totalMemoryUsage;

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
            public Channel<MessageEnvelope> MessageQueue { get; }
            public Task ProcessingTask { get; }
            public CancellationTokenSource CancellationTokenSource { get; }
            public DateTimeOffset CreatedAt { get; }

            public ActorInstance(IActor actor, Channel<MessageEnvelope> messageQueue, 
                Task processingTask, CancellationTokenSource cancellationTokenSource)
            {
                Actor = actor;
                MessageQueue = messageQueue;
                ProcessingTask = processingTask;
                CancellationTokenSource = cancellationTokenSource;
                CreatedAt = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Internal message envelope implementation for the in-memory runtime.
        /// </summary>
        private class MessageEnvelope : IMessageEnvelope
        {
            public string Id { get; }
            public object Payload { get; private set; }
            public IMessageMetadata Metadata { get; }
            public IReadOnlyDictionary<string, object> Headers { get; private set; }

            public MessageEnvelope(string id, object payload, IMessageMetadata metadata, 
                IReadOnlyDictionary<string, object>? headers = null)
            {
                Id = id;
                Payload = payload;
                Metadata = metadata;
                Headers = headers ?? new Dictionary<string, object>();
            }

            public IMessageEnvelope WithPayload(object newPayload)
            {
                return new MessageEnvelope(Id, newPayload, Metadata, Headers);
            }

            public IMessageEnvelope WithHeaders(IDictionary<string, object> additionalHeaders)
            {
                var newHeaders = new Dictionary<string, object>(Headers);
                foreach (var header in additionalHeaders)
                {
                    newHeaders[header.Key] = header.Value;
                }
                return new MessageEnvelope(Id, Payload, Metadata, newHeaders);
            }
        }

        /// <summary>
        /// Internal message metadata implementation for the in-memory runtime.
        /// </summary>
        private class MessageMetadata : IMessageMetadata
        {
            public string SenderId { get; }
            public string ReceiverId { get; }
            public DateTimeOffset Timestamp { get; }
            public string? CorrelationId { get; }
            public string? ReplyTo { get; }
            public int Priority { get; }
            public DateTimeOffset? ExpiresAt { get; }
            public string MessageType { get; }
            public string Version { get; }

            public MessageMetadata(string senderId, string receiverId, string messageType, 
                string? correlationId = null, string? replyTo = null, int priority = 0, 
                DateTimeOffset? expiresAt = null, string version = "1.0")
            {
                SenderId = senderId;
                ReceiverId = receiverId;
                Timestamp = DateTimeOffset.UtcNow;
                CorrelationId = correlationId;
                ReplyTo = replyTo;
                Priority = priority;
                ExpiresAt = expiresAt;
                MessageType = messageType;
                Version = version;
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
            var stopTasks = _actors.Keys.Select(actorId => StopActorAsync(actorId, cancellationToken)).ToArray();
            await Task.WhenAll(stopTasks);

            _isInitialized = false;
            LogTrace("InMemoryActorRuntime shutdown completed");
        }

        public async Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, 
            CancellationToken cancellationToken = default) where T : class, IActor
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (_actors.ContainsKey(actorId))
            {
                throw new InvalidOperationException($"Actor with ID '{actorId}' already exists");
            }

            LogTrace($"Spawning actor '{actorId}' of type '{typeof(T).Name}'");

            try
            {
                // Set initialization data in thread-local storage for agent setup
                if (initializationData is AgctorSDK.Core.Agents.AgentInitializationData agentInitData)
                {
                    _currentInitializationData.Value = agentInitData;
                }

                // Create actor instance using reflection or factory
                var actor = CreateActorInstance<T>(actorId);

                // Create message queue for the actor
                var messageQueue = Channel.CreateUnbounded<MessageEnvelope>();
                var actorCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(_shutdownTokenSource.Token);

                // Start message processing task for the actor
                var processingTask = ProcessActorMessagesAsync(actor, messageQueue.Reader, actorCancellationSource.Token);

                // Create actor instance wrapper
                var actorInstance = new ActorInstance(actor, messageQueue, processingTask, actorCancellationSource);

                // Register the actor
                if (!_actors.TryAdd(actorId, actorInstance))
                {
                    actorCancellationSource.Cancel();
                    throw new InvalidOperationException($"Failed to register actor '{actorId}'");
                }

                try
                {
                    // Initialize the actor
                    await actor.InitializeAsync(cancellationToken);
                    
                    LogTrace($"Actor '{actorId}' spawned and initialized successfully");
                    
                    // Fire event
                    ActorSpawned?.Invoke(this, new ActorSpawnedEventArgs(actorId, typeof(T).Name));
                    
                    return actor;
                }
                catch
                {
                    // Cleanup on failure
                    _actors.TryRemove(actorId, out _);
                    actorCancellationSource.Cancel();
                    throw;
                }
            }
            finally
            {
                // Clear thread-local initialization data
                _currentInitializationData.Value = null;
            }
        }

        public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, IActor
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (_actors.TryGetValue(actorId, out var actorInstance) && actorInstance.Actor is T typedActor)
            {
                LogTrace($"Retrieved actor '{actorId}' of type '{typeof(T).Name}'");
                return Task.FromResult<T?>(typedActor);
            }

            LogTrace($"Actor '{actorId}' not found or not of type '{typeof(T).Name}'");
            return Task.FromResult<T?>(null);
        }

        public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, 
            IDictionary<string, object>? headers = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (!_actors.TryGetValue(targetActorId, out var actorInstance))
            {
                throw new InvalidOperationException($"Target actor '{targetActorId}' not found");
            }

            var messageId = Guid.NewGuid().ToString();
            var metadata = new MessageMetadata(
                senderId ?? "system", 
                targetActorId, 
                message.GetType().Name);

            // Convert IDictionary to IReadOnlyDictionary
            IReadOnlyDictionary<string, object>? readOnlyHeaders = headers != null 
                ? new Dictionary<string, object>(headers) 
                : null;

            var envelope = new MessageEnvelope(messageId, message, metadata, readOnlyHeaders);

            LogTrace($"Sending message '{messageId}' from '{senderId ?? "system"}' to '{targetActorId}' (Type: {message.GetType().Name})");

            // Enqueue message to actor's queue
            if (!actorInstance.MessageQueue.Writer.TryWrite(envelope))
            {
                throw new InvalidOperationException($"Failed to enqueue message to actor '{targetActorId}'");
            }

            Interlocked.Increment(ref _totalMessagesProcessed);

            // Fire event
            MessageSent?.Invoke(this, new MessageSentEventArgs(messageId, senderId, targetActorId, message.GetType().Name));
            
            return Task.CompletedTask;
        }

        public async Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout,
            string? senderId = null, IDictionary<string, object>? headers = null, CancellationToken cancellationToken = default)
            where TResponse : class
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            var correlationId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<object>();
            
            // Register pending request
            _pendingRequests[correlationId] = tcs;

            try
            {
                // Add correlation ID to headers
                var requestHeaders = new Dictionary<string, object>(headers ?? new Dictionary<string, object>())
                {
                    ["CorrelationId"] = correlationId,
                    ["ReplyTo"] = senderId ?? "system"
                };

                // Send the message
                await SendMessageAsync(targetActorId, message, senderId, requestHeaders, cancellationToken);

                // Wait for response with timeout
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var responseTask = tcs.Task;
                var timeoutTask = Task.Delay(timeout, combinedCts.Token);

                var completedTask = await Task.WhenAny(responseTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException($"Request to actor '{targetActorId}' timed out after {timeout}");
                }

                var response = await responseTask;
                
                if (response is TResponse typedResponse)
                {
                    LogTrace($"Received response for correlation '{correlationId}' from '{targetActorId}'");
                    return typedResponse;
                }

                throw new InvalidOperationException($"Response type mismatch. Expected {typeof(TResponse).Name}, got {response?.GetType().Name ?? "null"}");
            }
            finally
            {
                _pendingRequests.TryRemove(correlationId, out _);
            }
        }

        public async Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (!_actors.TryRemove(actorId, out var actorInstance))
            {
                LogTrace($"Actor '{actorId}' not found for stopping");
                return;
            }

            LogTrace($"Stopping actor '{actorId}'");

            try
            {
                // Signal the actor to stop processing messages
                actorInstance.CancellationTokenSource.Cancel();

                // Close the message queue
                actorInstance.MessageQueue.Writer.Complete();

                // Shutdown the actor gracefully
                await actorInstance.Actor.ShutdownAsync(cancellationToken);

                // Wait for processing task to complete
                await actorInstance.ProcessingTask;

                LogTrace($"Actor '{actorId}' stopped successfully");

                // Fire event
                ActorStopped?.Invoke(this, new ActorStoppedEventArgs(actorId, actorInstance.Actor.ActorType, "Graceful shutdown"));
            }
            catch (Exception ex)
            {
                LogTrace($"Error stopping actor '{actorId}': {ex.Message}");
                ActorStopped?.Invoke(this, new ActorStoppedEventArgs(actorId, actorInstance.Actor.ActorType, $"Error during shutdown: {ex.Message}"));
            }
        }

        public async Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            var activeIds = _actors.Keys.ToList();
            LogTrace($"Retrieved {activeIds.Count} active actor IDs");
            return activeIds;
        }

        public async Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            var uptime = DateTimeOffset.UtcNow - _startTime;
            var messagesPerSecond = uptime.TotalSeconds > 0 ? _totalMessagesProcessed / uptime.TotalSeconds : 0;

            // Estimate memory usage (simplified)
            var estimatedMemory = _actors.Count * 1024 + _totalMessagesProcessed * 256;

            var stats = new RuntimeStatistics(
                activeActorCount: _actors.Count,
                totalMessagesProcessed: _totalMessagesProcessed,
                messagesPerSecond: messagesPerSecond,
                averageMessageProcessingTime: 5.0, // Simplified - would need actual measurement
                uptime: uptime,
                memoryUsageBytes: estimatedMemory,
                additionalMetrics: new Dictionary<string, object>
                {
                    ["PendingRequests"] = _pendingRequests.Count,
                    ["StartTime"] = _startTime,
                    ["RuntimeType"] = "InMemory"
                });

            return stats;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            LogTrace("Disposing InMemoryActorRuntime");

            try
            {
                if (_isInitialized)
                {
                    ShutdownAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                LogTrace($"Error during disposal: {ex.Message}");
            }

            _shutdownTokenSource?.Dispose();
            _isDisposed = true;
        }

        /// <summary>
        /// Creates an actor instance using reflection. In a real implementation,
        /// this could use dependency injection or actor factories.
        /// </summary>
        private T CreateActorInstance<T>(string actorId) where T : class, IActor
        {
            try
            {
                // Simple reflection-based creation - in production, use DI container
                var constructor = typeof(T).GetConstructor(new[] { typeof(string) });
                if (constructor != null)
                {
                    var instance = (T)constructor.Invoke(new object[] { actorId });
                    
                    // If this is an agent, set up additional properties
                    SetupAgentIfNeeded(instance, actorId);
                    
                    return instance;
                }

                // Try parameterless constructor
                var parameterlessConstructor = typeof(T).GetConstructor(Type.EmptyTypes);
                if (parameterlessConstructor != null)
                {
                    var instance = (T)parameterlessConstructor.Invoke(null);
                    
                    // Set ID via reflection if property exists
                    var idProperty = typeof(T).GetProperty("Id");
                    if (idProperty?.CanWrite == true)
                    {
                        idProperty.SetValue(instance, actorId);
                    }
                    
                    // If this is an agent, set up additional properties
                    SetupAgentIfNeeded(instance, actorId);
                    
                    return instance;
                }

                throw new InvalidOperationException($"No suitable constructor found for actor type {typeof(T).Name}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create actor instance of type {typeof(T).Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Sets up agent-specific properties if the actor is an agent.
        /// This includes setting the agent factory and parent agent ID from initialization data.
        /// </summary>
        private void SetupAgentIfNeeded<T>(T instance, string actorId) where T : class, IActor
        {
            // Check if this is an agent that needs additional setup
            if (instance is AgctorSDK.Core.Agents.Agent agent)
            {
                // Get initialization data from the current spawn context
                // In a more sophisticated implementation, this would be passed through the spawn context
                var initData = GetCurrentInitializationData();
                
                if (initData?.AgentFactory != null)
                {
                    agent.SetAgentFactory(initData.AgentFactory);
                }
                
                if (!string.IsNullOrEmpty(initData?.ParentAgentId))
                {
                    agent.SetParentAgentId(initData.ParentAgentId);
                }
            }
        }

        /// <summary>
        /// Gets the current initialization data for agent setup.
        /// This is a simplified implementation - in production, this would be managed through proper context.
        /// </summary>
        private AgctorSDK.Core.Agents.AgentInitializationData? GetCurrentInitializationData()
        {
            // For now, we'll store this in a thread-local variable during spawn operations
            // In a real implementation, this would be part of the spawn context
            return _currentInitializationData.Value;
        }

        // Thread-local storage for initialization data during spawn operations
        private readonly ThreadLocal<AgctorSDK.Core.Agents.AgentInitializationData?> _currentInitializationData = new();

        /// <summary>
        /// Processes messages for a specific actor in a dedicated task.
        /// This implements the per-actor message queue and dispatch mechanism.
        /// </summary>
        private async Task ProcessActorMessagesAsync(IActor actor, ChannelReader<MessageEnvelope> messageReader, 
            CancellationToken cancellationToken)
        {
            LogTrace($"Started message processing for actor '{actor.Id}'");

            try
            {
                await foreach (var envelope in messageReader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        LogTrace($"Processing message '{envelope.Id}' for actor '{actor.Id}' (Type: {envelope.Metadata.MessageType})");

                        var startTime = DateTimeOffset.UtcNow;
                        
                        // Dispatch message to actor
                        await actor.ReceiveAsync(envelope, cancellationToken);
                        
                        var processingTime = DateTimeOffset.UtcNow - startTime;
                        LogTrace($"Message '{envelope.Id}' processed by actor '{actor.Id}' in {processingTime.TotalMilliseconds:F2}ms");

                        // Handle response for request-response pattern
                        if (envelope.Headers.TryGetValue("CorrelationId", out var correlationIdObj) &&
                            correlationIdObj is string correlationId &&
                            _pendingRequests.TryGetValue(correlationId, out var tcs))
                        {
                            // For simplicity, we'll assume the actor sends a response message
                            // In a real implementation, actors would have a way to send responses
                            LogTrace($"Handling response for correlation '{correlationId}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogTrace($"Error processing message '{envelope.Id}' for actor '{actor.Id}': {ex.Message}");
                        
                        // Handle response error for request-response pattern
                        if (envelope.Headers.TryGetValue("CorrelationId", out var correlationIdObj) &&
                            correlationIdObj is string correlationId &&
                            _pendingRequests.TryRemove(correlationId, out var tcs))
                        {
                            tcs.SetException(ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogTrace($"Message processing cancelled for actor '{actor.Id}'");
            }
            catch (Exception ex)
            {
                LogTrace($"Fatal error in message processing for actor '{actor.Id}': {ex.Message}");
            }

            LogTrace($"Message processing ended for actor '{actor.Id}'");
        }

        /// <summary>
        /// Requests human input via the console.
        /// Implements the CLI interaction specified in prd-cli-001.md.
        /// </summary>
        /// <param name="requestingAgentId">The ID of the agent requesting human input.</param>
        /// <param name="prompt">The prompt or question to display to the human.</param>
        /// <param name="instructions">Instructions for the human on how to submit their input (e.g., end token).</param>
        /// <param name="cancellationToken">Token for cancelling the operation.</param>
        /// <returns>A task containing the string input provided by the human.</returns>
        public Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default)
        {
            // Log the request for human input
            Console.WriteLine($"[INFO] Agent {requestingAgentId} is requesting human input.");
            Console.WriteLine($"[HUMAN INPUT PROMPT] {prompt}");
            Console.WriteLine(instructions); // e.g., "Please enter your suggestion below (type \"::done\" on a new line to finish):"

            var inputLines = new List<string>();
            string? line;

            // Read multiline input from the console
            // Loop until "::done" is entered or cancellation is requested
            while (!cancellationToken.IsCancellationRequested)
            {
                line = Console.ReadLine();
                if (line == "::done")
                {
                    break;
                }
                if (line != null)
                {
                    inputLines.Add(line);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                // Log cancellation and throw if requested
                Console.WriteLine("[INFO] Human input request was cancelled.");
                throw new OperationCanceledException(cancellationToken);
            }

            var humanResponse = string.Join(Environment.NewLine, inputLines);
            // Log the received human input
            Console.WriteLine($"[INFO] Human input received from user for agent {requestingAgentId}.");
            return Task.FromResult(humanResponse);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(InMemoryActorRuntime));
        }

        private void ThrowIfNotInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Runtime is not initialized. Call InitializeAsync first.");
        }

        /// <summary>
        /// Simple trace logging to stdout for debugging and monitoring.
        /// In production, this would integrate with a proper logging framework.
        /// </summary>
        private void LogTrace(string message)
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Console.WriteLine($"[{timestamp}] [InMemoryActorRuntime] {message}");
        }
    }

    /// <summary>
    /// Implementation of IRuntimeStatistics for the in-memory runtime.
    /// </summary>
    internal class RuntimeStatistics : IRuntimeStatistics
    {
        public int ActiveActorCount { get; }
        public long TotalMessagesProcessed { get; }
        public double MessagesPerSecond { get; }
        public double AverageMessageProcessingTime { get; }
        public TimeSpan Uptime { get; }
        public long MemoryUsageBytes { get; }
        public IReadOnlyDictionary<string, object> AdditionalMetrics { get; }

        public RuntimeStatistics(int activeActorCount, long totalMessagesProcessed, double messagesPerSecond,
            double averageMessageProcessingTime, TimeSpan uptime, long memoryUsageBytes,
            IReadOnlyDictionary<string, object> additionalMetrics)
        {
            ActiveActorCount = activeActorCount;
            TotalMessagesProcessed = totalMessagesProcessed;
            MessagesPerSecond = messagesPerSecond;
            AverageMessageProcessingTime = averageMessageProcessingTime;
            Uptime = uptime;
            MemoryUsageBytes = memoryUsageBytes;
            AdditionalMetrics = additionalMetrics;
        }
    }
} 