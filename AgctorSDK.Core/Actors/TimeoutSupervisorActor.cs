using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Actors
{
    /// <summary>
    /// Actor responsible for managing timeout monitoring and handling for agent operations.
    /// Uses message-based communication to schedule and manage timeouts without polling.
    /// Supports timeout policies, partial result collection, and budget management.
    /// </summary>
    public class TimeoutSupervisorActor : ITimeoutSupervisor
    {
        /// <summary>
        /// Information about a monitored operation.
        /// </summary>
        private class MonitoredOperation
        {
            public string AgentId { get; }
            public string OperationId { get; }
            public AgentContext Context { get; }
            public ITimeoutPolicy? TimeoutPolicy { get; }
            public DateTimeOffset StartTime { get; }
            public DateTimeOffset LastProgressUpdate { get; set; }
            public AgentProgress? LastProgress { get; set; }
            public int RescheduleCount { get; set; }
            public bool IsActive { get; set; } = true;

            public MonitoredOperation(string agentId, string operationId, AgentContext context, ITimeoutPolicy? timeoutPolicy = null)
            {
                AgentId = agentId;
                OperationId = operationId;
                Context = context;
                TimeoutPolicy = timeoutPolicy;
                StartTime = DateTimeOffset.UtcNow;
                LastProgressUpdate = StartTime;
            }
        }

        private readonly ILogger<TimeoutSupervisorActor> _logger;
        private readonly IActorRuntimeAdapter _runtimeAdapter;
        private readonly TimeoutSupervisorOptions _options;
        private readonly ITimeoutPolicy _defaultTimeoutPolicy;
        private readonly ConcurrentDictionary<string, MonitoredOperation> _monitoredOperations;

        public string Id { get; }
        public string ActorType => nameof(TimeoutSupervisorActor);
        public ActorState State { get; private set; } = ActorState.Initializing;

        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        public TimeoutSupervisorActor(
            string id,
            IActorRuntimeAdapter runtimeAdapter,
            ITimeoutPolicy defaultTimeoutPolicy,
            TimeoutSupervisorOptions? options = null,
            ILogger<TimeoutSupervisorActor>? logger = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            _runtimeAdapter = runtimeAdapter ?? throw new ArgumentNullException(nameof(runtimeAdapter));
            _defaultTimeoutPolicy = defaultTimeoutPolicy ?? throw new ArgumentNullException(nameof(defaultTimeoutPolicy));
            _options = options ?? new TimeoutSupervisorOptions();
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TimeoutSupervisorActor>.Instance;
            _monitoredOperations = new ConcurrentDictionary<string, MonitoredOperation>();
        }

        public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            try
            {
                return envelope.Payload switch
                {
                    RegisterTimeoutMessage msg => await HandleRegisterTimeoutAsync(msg, cancellationToken),
                    CancelTimeoutMessage msg => await HandleCancelTimeoutAsync(msg, cancellationToken),
                    UpdateProgressMessage msg => await HandleUpdateProgressAsync(msg, cancellationToken),
                    TimeoutTriggerMessage msg => await HandleTimeoutTriggerAsync(msg, cancellationToken),
                    CheckTimeoutMessage msg => await HandleCheckTimeoutAsync(msg, cancellationToken),
                    CollectPartialResultsMessage msg => await HandleCollectPartialResultsAsync(msg, cancellationToken),
                    PartialResultsResponse msg => await HandlePartialResultsResponseAsync(msg, cancellationToken),
                    _ => await HandleUnknownMessageAsync(envelope, cancellationToken)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageType} in TimeoutSupervisor {ActorId}", 
                    envelope.Payload.GetType().Name, Id);
                
                // Return error response
                return CreateResponseEnvelope(new { Error = ex.Message, MessageType = envelope.Payload.GetType().Name });
            }
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var previousState = State;
            State = ActorState.Active;
            
            _logger.LogInformation("TimeoutSupervisorActor {ActorId} initialized successfully", Id);
            
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previousState, State, "Initialization completed"));
            
            await Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            var previousState = State;
            State = ActorState.Stopping;
            
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previousState, State, "Shutdown initiated"));
            
            // Cancel all monitored operations
            foreach (var operation in _monitoredOperations.Values)
            {
                if (operation.IsActive)
                {
                    await NotifyTimeoutAsync(operation, TimeoutAction.Cancel, "Supervisor shutdown", cancellationToken);
                }
            }
            
            _monitoredOperations.Clear();
            
            State = ActorState.Stopped;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(ActorState.Stopping, State, "Shutdown completed"));
            
            _logger.LogInformation("TimeoutSupervisorActor {ActorId} shutdown completed", Id);
        }

        public async Task RegisterTimeoutAsync(string agentId, string operationId, AgentContext context, CancellationToken cancellationToken = default)
        {
            var envelope = new MessageEnvelope(new RegisterTimeoutMessage(agentId, operationId, context));
            await ReceiveAsync(envelope, cancellationToken);
        }

        public async Task CancelTimeoutAsync(string agentId, string operationId, CancellationToken cancellationToken = default)
        {
            var envelope = new MessageEnvelope(new CancelTimeoutMessage(agentId, operationId, "Operation completed"));
            await ReceiveAsync(envelope, cancellationToken);
        }

        public async Task UpdateProgressAsync(string agentId, string operationId, AgentProgress progress, CancellationToken cancellationToken = default)
        {
            var envelope = new MessageEnvelope(new UpdateProgressMessage(agentId, operationId, progress));
            await ReceiveAsync(envelope, cancellationToken);
        }

        public async Task CheckTimeoutAsync(string agentId, string operationId, CancellationToken cancellationToken = default)
        {
            var envelope = new MessageEnvelope(new CheckTimeoutMessage(agentId, operationId));
            await ReceiveAsync(envelope, cancellationToken);
        }

        private async Task<IMessageEnvelope> HandleRegisterTimeoutAsync(RegisterTimeoutMessage message, CancellationToken cancellationToken)
        {
            var operationKey = GetOperationKey(message.AgentId, message.OperationId);
            var operation = new MonitoredOperation(message.AgentId, message.OperationId, message.Context, message.TimeoutPolicy);
            
            _monitoredOperations.AddOrUpdate(operationKey, operation, (key, existing) => operation);
            
            // Calculate timeout using policy
            var policy = message.TimeoutPolicy ?? _defaultTimeoutPolicy;
            var timeout = policy.GetTimeout(message.Context);
            if (timeout <= TimeSpan.Zero)
            {
                timeout = _options.DefaultTimeout;
            }
            
            // Schedule timeout trigger message
            await ScheduleTimeoutTriggerAsync(message.AgentId, message.OperationId, timeout, cancellationToken);
            
            if (_options.EnableTimeoutLogging)
            {
                _logger.LogInformation("Registered timeout for agent {AgentId} operation {OperationId} with timeout {Timeout}",
                    message.AgentId, message.OperationId, timeout);
            }
            
            return CreateResponseEnvelope(new { Success = true, Timeout = timeout });
        }

        private async Task<IMessageEnvelope> HandleCancelTimeoutAsync(CancelTimeoutMessage message, CancellationToken cancellationToken)
        {
            var operationKey = GetOperationKey(message.AgentId, message.OperationId);
            
            if (_monitoredOperations.TryRemove(operationKey, out var operation))
            {
                operation.IsActive = false;
                
                if (_options.EnableTimeoutLogging)
                {
                    _logger.LogInformation("Cancelled timeout monitoring for agent {AgentId} operation {OperationId}: {Reason}",
                        message.AgentId, message.OperationId, message.Reason);
                }
            }
            
            await Task.CompletedTask;
            return CreateResponseEnvelope(new { Success = true });
        }

        private async Task<IMessageEnvelope> HandleUpdateProgressAsync(UpdateProgressMessage message, CancellationToken cancellationToken)
        {
            var operationKey = GetOperationKey(message.AgentId, message.OperationId);
            
            if (_monitoredOperations.TryGetValue(operationKey, out var operation) && operation.IsActive)
            {
                operation.LastProgress = message.Progress;
                operation.LastProgressUpdate = DateTimeOffset.UtcNow;
                
                // Check if timeout should be rescheduled based on progress
                var policy = operation.TimeoutPolicy ?? _defaultTimeoutPolicy;
                if (policy.ShouldReschedule(operation.Context, message.Progress) && 
                    operation.RescheduleCount < _options.MaxRescheduleCount)
                {
                    operation.RescheduleCount++;
                    var newTimeout = policy.GetTimeout(operation.Context);
                    await ScheduleTimeoutTriggerAsync(message.AgentId, message.OperationId, newTimeout, cancellationToken, operation.RescheduleCount);
                    
                    if (_options.EnableTimeoutLogging)
                    {
                        _logger.LogInformation("Rescheduled timeout for agent {AgentId} operation {OperationId} (attempt {Count})",
                            message.AgentId, message.OperationId, operation.RescheduleCount);
                    }
                }
            }
            
            await Task.CompletedTask;
            return CreateResponseEnvelope(new { Success = true });
        }

        private async Task<IMessageEnvelope> HandleTimeoutTriggerAsync(TimeoutTriggerMessage message, CancellationToken cancellationToken)
        {
            var operationKey = GetOperationKey(message.AgentId, message.OperationId);
            
            if (!_monitoredOperations.TryGetValue(operationKey, out var operation) || !operation.IsActive)
            {
                // Operation was already cancelled or completed
                return CreateResponseEnvelope(new { Success = true, Reason = "Operation no longer active" });
            }
            
            // Check if this timeout is still valid (not superseded by a reschedule)
            if (message.RescheduleCount < operation.RescheduleCount)
            {
                // This is an old timeout trigger that was rescheduled
                return CreateResponseEnvelope(new { Success = true, Reason = "Timeout was rescheduled" });
            }
            
            var policy = operation.TimeoutPolicy ?? _defaultTimeoutPolicy;
            
            // Check if operation should be aborted immediately
            if (policy.ShouldAbort(operation.Context, ActorState.Active))
            {
                await NotifyTimeoutAsync(operation, TimeoutAction.Abort, "Policy determined operation should be aborted", cancellationToken);
            }
            else if (_options.CollectPartialResultsOnTimeout)
            {
                // Try to collect partial results before timing out
                await CollectPartialResultsAndTimeoutAsync(operation, cancellationToken);
            }
            else
            {
                // Simple timeout without partial results
                await NotifyTimeoutAsync(operation, TimeoutAction.Cancel, "Operation timed out", cancellationToken);
            }
            
            return CreateResponseEnvelope(new { Success = true });
        }

        private async Task<IMessageEnvelope> HandleCheckTimeoutAsync(CheckTimeoutMessage message, CancellationToken cancellationToken)
        {
            var operationKey = GetOperationKey(message.AgentId, message.OperationId);
            
            if (_monitoredOperations.TryGetValue(operationKey, out var operation) && operation.IsActive)
            {
                if (message.ForceTimeout)
                {
                    await NotifyTimeoutAsync(operation, TimeoutAction.Cancel, "Forced timeout check", cancellationToken);
                }
                else
                {
                    // Create a timeout trigger message and handle it
                    var triggerMessage = new TimeoutTriggerMessage(
                        message.AgentId, 
                        message.OperationId, 
                        DateTimeOffset.UtcNow, 
                        TimeSpan.Zero, 
                        operation.RescheduleCount);
                    
                    await HandleTimeoutTriggerAsync(triggerMessage, cancellationToken);
                }
            }
            
            await Task.CompletedTask;
            return CreateResponseEnvelope(new { Success = true });
        }

        private async Task<IMessageEnvelope> HandleCollectPartialResultsAsync(CollectPartialResultsMessage message, CancellationToken cancellationToken)
        {
            // Forward the message to the target agent
            await _runtimeAdapter.SendMessageAsync(message.AgentId, message, Id);
            return CreateResponseEnvelope(new { Success = true });
        }

        private async Task<IMessageEnvelope> HandlePartialResultsResponseAsync(PartialResultsResponse message, CancellationToken cancellationToken)
        {
            var operationKey = GetOperationKey(message.AgentId, message.OperationId);
            
            if (_monitoredOperations.TryGetValue(operationKey, out var operation) && operation.IsActive)
            {
                // Create timeout result with partial results
                var result = new TimeoutResult(
                    TimeoutAction.Cancel,
                    message.PartialResults,
                    "Timeout with partial results collected",
                    DateTimeOffset.UtcNow - operation.StartTime);
                
                await NotifyTimeoutAsync(operation, result, cancellationToken);
            }
            
            await Task.CompletedTask;
            return CreateResponseEnvelope(new { Success = true });
        }

        private async Task<IMessageEnvelope> HandleUnknownMessageAsync(IMessageEnvelope envelope, CancellationToken cancellationToken)
        {
            _logger.LogWarning("TimeoutSupervisorActor {ActorId} received unknown message type: {MessageType}",
                Id, envelope.Payload.GetType().Name);
            
            await Task.CompletedTask;
            return CreateResponseEnvelope(new { Error = "Unknown message type", MessageType = envelope.Payload.GetType().Name });
        }

        private async Task ScheduleTimeoutTriggerAsync(string agentId, string operationId, TimeSpan delay, CancellationToken cancellationToken, int rescheduleCount = 0)
        {
            var triggerMessage = new TimeoutTriggerMessage(agentId, operationId, DateTimeOffset.UtcNow, delay, rescheduleCount);
            
            // Since the runtime doesn't have a ScheduleMessageAsync method, we'll use Task.Delay
            // This is not ideal for a production actor system but works for demonstration
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        var envelope = new MessageEnvelope(triggerMessage);
                        await ReceiveAsync(envelope, CancellationToken.None);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in scheduled timeout trigger for agent {AgentId} operation {OperationId}",
                        agentId, operationId);
                }
            }, cancellationToken);
            
            await Task.CompletedTask;
        }

        private async Task CollectPartialResultsAndTimeoutAsync(MonitoredOperation operation, CancellationToken cancellationToken)
        {
            try
            {
                var collectMessage = new CollectPartialResultsMessage(
                    operation.AgentId, 
                    operation.OperationId, 
                    _options.PartialResultsGracePeriod);
                
                // Send partial results collection request with timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.PartialResultsGracePeriod);
                
                await _runtimeAdapter.SendMessageAsync(operation.AgentId, collectMessage, Id, null, timeoutCts.Token);
                
                // Note: The actual timeout will be handled when PartialResultsResponse is received
                // or when the grace period expires
            }
            catch (OperationCanceledException)
            {
                // Grace period expired, timeout without partial results
                await NotifyTimeoutAsync(operation, TimeoutAction.Cancel, "Timeout - partial results collection failed", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting partial results for agent {AgentId} operation {OperationId}",
                    operation.AgentId, operation.OperationId);
                
                await NotifyTimeoutAsync(operation, TimeoutAction.Cancel, $"Timeout - error collecting partial results: {ex.Message}", cancellationToken);
            }
        }

        private async Task NotifyTimeoutAsync(MonitoredOperation operation, TimeoutAction action, string details, CancellationToken cancellationToken)
        {
            var result = new TimeoutResult(action, operation.LastProgress?.PartialResults, details, DateTimeOffset.UtcNow - operation.StartTime);
            await NotifyTimeoutAsync(operation, result, cancellationToken);
        }

        private async Task NotifyTimeoutAsync(MonitoredOperation operation, TimeoutResult result, CancellationToken cancellationToken)
        {
            // Mark operation as inactive
            operation.IsActive = false;
            var operationKey = GetOperationKey(operation.AgentId, operation.OperationId);
            _monitoredOperations.TryRemove(operationKey, out _);
            
            // Create timeout notification message
            var timeoutMessage = new TimeoutOccurredMessage(
                operation.AgentId,
                operation.OperationId,
                result,
                operation.Context,
                operation.Context.ParentAgentId);
            
            try
            {
                // Notify the agent that timed out
                await _runtimeAdapter.SendMessageAsync(operation.AgentId, timeoutMessage, Id, null, cancellationToken);
                
                // Notify parent agent if configured and parent exists
                if (_options.NotifyParentOnChildTimeout && !string.IsNullOrEmpty(operation.Context.ParentAgentId))
                {
                    await _runtimeAdapter.SendMessageAsync(operation.Context.ParentAgentId, timeoutMessage, Id, null, cancellationToken);
                }
                
                if (_options.EnableTimeoutLogging)
                {
                    _logger.LogWarning("Timeout occurred for agent {AgentId} operation {OperationId}: {Action} - {Details}",
                        operation.AgentId, operation.OperationId, result.Action, result.Details);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying timeout for agent {AgentId} operation {OperationId}",
                    operation.AgentId, operation.OperationId);
            }
        }

        private static string GetOperationKey(string agentId, string operationId) => $"{agentId}:{operationId}";

        private static IMessageEnvelope CreateResponseEnvelope(object payload)
        {
            return new MessageEnvelope(payload);
        }
    }
} 