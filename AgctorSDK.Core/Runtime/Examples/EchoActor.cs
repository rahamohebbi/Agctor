using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using System.Collections.Generic;

namespace AgctorSDK.Core.Runtime.Examples
{
    /// <summary>
    /// Simple echo actor that demonstrates basic message handling.
    /// This actor receives messages and echoes them back with additional information.
    /// </summary>
    public class EchoActor : IActor
    {
        private ActorState _state = ActorState.Initializing;
        private int _messageCount = 0;

        public string Id { get; }
        public string ActorType => nameof(EchoActor);
        public ActorState State => _state;

        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        public EchoActor(string id)
        {
            Id = id;
            LogTrace($"EchoActor '{id}' created");
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            LogTrace($"Initializing EchoActor '{Id}'");
            
            ChangeState(ActorState.Active, "Initialization completed");
            
            LogTrace($"EchoActor '{Id}' initialized successfully");
            
            return Task.CompletedTask;
        }

        public async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _messageCount++;
            string originalPayloadString = envelope.Payload?.ToString() ?? "null_payload";
            string incomingMessageType = "UnknownType";
            if (envelope.Headers?.TryGetValue("MessageType", out var mt) == true) 
            {
                incomingMessageType = mt;
            }
            LogTrace($"EchoActor '{Id}' received message #{_messageCount}: '{originalPayloadString}' (Type Header: {incomingMessageType})");

            await Task.Delay(10, cancellationToken);

            object responsePayload;
            switch (envelope.Payload)
            {
                case string textMessage:
                    LogTrace($"EchoActor '{Id}' echoing text message: '{textMessage}'");
                    responsePayload = new EchoResponse(textMessage, Id, _messageCount);
                    break;
                    
                case int numberMessage:
                    LogTrace($"EchoActor '{Id}' echoing number message: {numberMessage}");
                    responsePayload = new EchoResponse(numberMessage.ToString(), Id, _messageCount);
                    break;
                    
                case EchoRequest request:
                    LogTrace($"EchoActor '{Id}' processing echo request: '{request.Message}' with delay {request.DelayMs}ms");
                    if (request.DelayMs > 0)
                    {
                        await Task.Delay(request.DelayMs, cancellationToken);
                    }
                    responsePayload = new EchoResponse(request.Message, Id, _messageCount);
                    break;
                    
                default:
                    LogTrace($"EchoActor '{Id}' received unknown message type: {envelope.Payload?.GetType().Name ?? "null"}");
                    responsePayload = new EchoResponse($"Unknown message type: {originalPayloadString}", Id, _messageCount);
                    break;
            }

            LogTrace($"EchoActor '{Id}' finished processing message #{_messageCount}");

            // Prepare MCP-compliant response envelope
            string? requestSenderId = null;
            if (envelope.Headers?.TryGetValue("SenderId", out var sid) == true) requestSenderId = sid;

            string? requestCorrelationId = null;
            if (envelope.Metadata?.TryGetValue("CorrelationId", out var corrIdObj) == true && corrIdObj is string corrIdStr) requestCorrelationId = corrIdStr;

            var responseMetadata = new Dictionary<string, object>
            {
                { "Timestamp", DateTimeOffset.UtcNow }
            };
            if (requestCorrelationId != null) responseMetadata["CorrelationId"] = requestCorrelationId;

            var responseHeaders = new Dictionary<string, string>
            {
                { "SenderId", Id },
                { "ReceiverId", requestSenderId ?? "unknown" }, // Default to unknown if not present
                { "MessageType", "EchoResponse" }, // Specific message type for the response
                { "Version", "1.0" }
            };
            
            // Use the main MessageEnvelope from AgctorSDK.Core.Messages
            return new AgctorSDK.Core.Messages.MessageEnvelope(
                payload: responsePayload, 
                metadata: responseMetadata, 
                id: Guid.NewGuid().ToString(), // New unique ID for the response envelope
                headers: responseHeaders
            );
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            LogTrace($"Shutting down EchoActor '{Id}' (processed {_messageCount} messages)");
            
            ChangeState(ActorState.Stopping, "Shutdown initiated");
            
            await Task.Delay(5, cancellationToken);
            
            ChangeState(ActorState.Stopped, "Shutdown completed");
            
            LogTrace($"EchoActor '{Id}' shutdown completed");
        }

        private void ChangeState(ActorState newState, string? reason = null)
        {
            var previousState = _state;
            _state = newState;
            
            LogTrace($"EchoActor '{Id}' state changed: {previousState} -> {newState} ({reason})");
            
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previousState, newState, reason));
        }

        private void LogTrace(string message)
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Console.WriteLine($"[{timestamp}] [EchoActor] {message}");
        }
    }

    /// <summary>
    /// Example message type for demonstrating structured message handling.
    /// </summary>
    public class EchoRequest
    {
        public string Message { get; set; } = string.Empty;
        public int DelayMs { get; set; } = 0;
        public string? Metadata { get; set; }

        public EchoRequest() { }

        public EchoRequest(string message, int delayMs = 0, string? metadata = null)
        {
            Message = message;
            DelayMs = delayMs;
            Metadata = metadata;
        }

        public override string ToString()
        {
            return $"EchoRequest(Message='{Message}', DelayMs={DelayMs}, Metadata='{Metadata}')";
        }
    }

    /// <summary>
    /// Example response message type for request-response patterns.
    /// </summary>
    public class EchoResponse
    {
        public string OriginalMessage { get; set; } = string.Empty;
        public string EchoedMessage { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public DateTimeOffset ProcessedAt { get; set; }
        public int MessageNumber { get; set; }

        public EchoResponse() { }

        public EchoResponse(string originalMessage, string actorId, int messageNumber)
        {
            OriginalMessage = originalMessage;
            EchoedMessage = $"Echo: {originalMessage}";
            ActorId = actorId;
            ProcessedAt = DateTimeOffset.UtcNow;
            MessageNumber = messageNumber;
        }

        public override string ToString()
        {
            return $"EchoResponse(Original='{OriginalMessage}', Echoed='{EchoedMessage}', Actor='{ActorId}', #={MessageNumber})";
        }
    }
} 