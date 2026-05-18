using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using Proto;
using System.Collections.Concurrent;
using AgctorIActor = AgctorSDK.Core.Interfaces.IActor;
using ProtoActorInterface = Proto.IActor;
using Proto.Remote;
using Proto.Remote.GrpcNet;
using System.IO;
using System.Text.Json;
using AgctorSDK.Core.Messages;

namespace AgctorSDK.Core.Adapters
{
    /// <summary>
    /// <strong>Experimental.</strong> Proto.Actor runtime adapter (partial implementation).
    /// Enable via <c>Agctor:AllowExperimentalRuntimes=true</c>; default host/CLI use InMemory.
    /// </summary>
    public class ProtoActorAdapter : IActorRuntimeAdapter
    {
        private bool _isDisposed;
#pragma warning disable CS0649 // Field is never assigned to
        private bool _isInitialized;
#pragma warning restore CS0649
        private readonly Dictionary<string, object> _configuration = new();
        private ActorSystem _system = null!;
        private Proto.IRootContext _root = null!;
        private readonly ConcurrentDictionary<string, PID> _pidMap = new();
        private readonly ConcurrentDictionary<string, AgctorIActor> _actorInstances = new();
        internal readonly ConcurrentDictionary<string, TaskCompletionSource<IMessageEnvelope>> _pendingRequests = new();
        private PID _replyPid = null!; // Local proxy actor that receives all replies
        private DateTimeOffset _startTime;
        private long _totalMessages;
        private readonly ConcurrentDictionary<string, long> _actorMsgCount = new();
        private GrpcNetRemote? _remote;

        /// <summary>
        /// Name identifier for the Proto.Actor runtime adapter.
        /// </summary>
        public string Name => "Proto.Actor";

        /// <summary>
        /// Version of the Proto.Actor adapter implementation.
        /// </summary>
        public string Version => "1.0.0-placeholder";

        /// <summary>
        /// Indicates whether the Proto.Actor runtime is initialized and ready.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Configuration properties specific to Proto.Actor runtime.
        /// </summary>
        public IReadOnlyDictionary<string, object> Configuration => _configuration;

        /// <summary>
        /// Event raised when an actor is spawned in Proto.Actor.
        /// </summary>
#pragma warning disable CS0067 // Event is never used
        public event EventHandler<ActorSpawnedEventArgs>? ActorSpawned;
#pragma warning restore CS0067

        /// <summary>
        /// Event raised when an actor is stopped in Proto.Actor.
        /// </summary>
#pragma warning disable CS0067 // Event is never used
        public event EventHandler<ActorStoppedEventArgs>? ActorStopped;
#pragma warning restore CS0067

        /// <summary>
        /// Event raised when a message is sent through Proto.Actor.
        /// </summary>
#pragma warning disable CS0067 // Event is never used
        public event EventHandler<MessageSentEventArgs>? MessageSent;
#pragma warning restore CS0067

        public event EventHandler<DeadLetterEventArgs>? DeadLetter;

        /// <summary>
        /// Initializes the Proto.Actor runtime with the provided configuration.
        /// TODO: Implement Proto.Actor system initialization and actor system setup.
        /// </summary>
        public async Task InitializeAsync(IDictionary<string, object> configuration, CancellationToken cancellationToken = default)
        {
            if (_isInitialized) return;
            foreach (var kvp in configuration) _configuration[kvp.Key] = kvp.Value;

            string? host = _configuration.TryGetValue("remoteHost", out var h) ? h?.ToString() : null;
            int port = _configuration.TryGetValue("remotePort", out var p) && int.TryParse(p.ToString(), out var po) ? po : 0;

            _system = new ActorSystem();

            if (!string.IsNullOrEmpty(host) && port > 0)
            {
                try
                {
                    var remoteConfig = GrpcNetRemoteConfig.BindTo(host!, port);
                    _remote = new GrpcNetRemote(_system, remoteConfig);
                    await _remote.StartAsync();
                }
                catch (IOException ioEx) when (ioEx.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException)
                {
                    // The requested port is already in use – fall back to an ephemeral port
                    LogWarning($"Port {port} is already in use, falling back to a dynamic port.");
                    _remote = null; // reset before retry

                    // Attempt to find a free TCP port
                    int freePort = GetAvailablePort();
                    _configuration["remotePort"] = freePort; // persist chosen port for later discovery

                    var remoteConfig = GrpcNetRemoteConfig.BindTo(host!, freePort);
                    _remote = new GrpcNetRemote(_system, remoteConfig);
                    await _remote.StartAsync();
                }
            }

            _root = _system.Root;

            // Spawn reply proxy for correlation matching
            var proxyProps = Props.FromProducer(() => new ReplyProxy(this));
            _replyPid = _root.SpawnNamed(proxyProps, "agctor-reply-proxy");

            _startTime = DateTimeOffset.UtcNow;
            _isInitialized = true;
        }

        /// <summary>
        /// Gracefully shuts down the Proto.Actor runtime and cleans up resources.
        /// TODO: Implement Proto.Actor system shutdown and resource cleanup.
        /// </summary>
        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            if (!_isInitialized) return;
            foreach (var pid in _pidMap.Values)
            {
                _root.Stop(pid);
            }
            _pidMap.Clear();
            // _system.Dispose(); // ActorSystem has no Dispose; allow GC
            _isInitialized = false;
            await Task.CompletedTask;
            if(_remote!=null)
            {
                await _remote.ShutdownAsync();
                _remote=null;
            }
        }

        /// <summary>
        /// Spawns a new Proto.Actor instance of the specified type.
        /// TODO: Implement Proto.Actor spawning using Props and actor system.
        /// </summary>
        public async Task<T> SpawnActorAsync<T>(string actorId, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, AgctorIActor
        {
            return await SpawnActorAsync(actorId, id => CreateActorInstance<T>(id), initializationData, cancellationToken);
        }

        public async Task<T> SpawnActorAsync<T>(string actorId, Func<string, T> actorFactory, object? initializationData = null, CancellationToken cancellationToken = default) where T : class, AgctorIActor
        {
            if (!_isInitialized) throw new InvalidOperationException("Runtime not initialized");
            if (string.IsNullOrWhiteSpace(actorId)) throw new ArgumentException("actorId cannot be null or empty");
            if (_pidMap.ContainsKey(actorId)) throw new InvalidOperationException($"Actor with id {actorId} already exists");

            var actorInstance = actorFactory(actorId);
            await actorInstance.InitializeAsync(cancellationToken);

            var props = Props.FromProducer(() => new ProtoActorShell(actorInstance, this));
            var pid = _root.SpawnNamed(props, actorId);
            if(!_pidMap.TryAdd(actorId, pid))
            {
                _root.Stop(pid);
                throw new InvalidOperationException("Failed to add PID map");
            }
            _actorInstances[actorId]=actorInstance;
            ActorSpawned?.Invoke(this, new ActorSpawnedEventArgs(actorId, typeof(T).Name));
            return actorInstance;
        }

        /// <summary>
        /// Registers an existing actor instance with the Proto.Actor runtime.
        /// TODO: Implement Proto.Actor registration logic.
        /// </summary>
        public async Task RegisterActorAsync(AgctorIActor actor, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized) throw new InvalidOperationException("Runtime not initialized");
            if (actor is null) throw new ArgumentNullException(nameof(actor));
            var actorId = actor.Id;
            if (_pidMap.ContainsKey(actorId)) throw new InvalidOperationException($"Actor with id {actorId} already registered");

            // Ensure actor is initialized
            if (actor.State == ActorState.Initializing)
            {
                await actor.InitializeAsync(cancellationToken);
            }

            var props = Props.FromProducer(() => new ProtoActorShell(actor, this));
            var pid = _root.SpawnNamed(props, actorId);
            if(!_pidMap.TryAdd(actorId, pid))
            {
                _root.Stop(pid);
                throw new InvalidOperationException("Failed to register PID");
            }
            _actorInstances[actorId]=actor;
            ActorSpawned?.Invoke(this, new ActorSpawnedEventArgs(actorId, actor.ActorType));
        }

        /// <summary>
        /// Gets a reference to an existing Proto.Actor by its ID.
        /// TODO: Implement Proto.Actor PID resolution and actor reference retrieval.
        /// </summary>
        public Task<T?> GetActorAsync<T>(string actorId, CancellationToken cancellationToken = default) where T : class, AgctorIActor
        {
            if (_actorInstances.TryGetValue(actorId, out var actor))
            {
                return Task.FromResult(actor as T);
            }
            return Task.FromResult<T?>(null);
        }

        /// <summary>
        /// Sends a message to the specified Proto.Actor.
        /// TODO: Implement Proto.Actor message sending using PID and context.
        /// </summary>
        public Task SendMessageAsync(string targetActorId, object message, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized) throw new InvalidOperationException("Runtime not initialized");
            IMessageEnvelope envelope;
            if (message is IMessageEnvelope msgEnv)
            {
                envelope = msgEnv;
            }
            else
            {
                var correlationId = headers != null && headers.TryGetValue(AgctorMessageHeaders.CorrelationId, out var corr)
                    ? corr
                    : null;
                envelope = AgctorEnvelopeBuilder.Command(
                    message,
                    senderId ?? "system",
                    targetActorId,
                    message is string ? AgctorMessageTypes.Prompt : message?.GetType().Name,
                    correlationId,
                    headers == null ? null : new Dictionary<string, string>(headers));
            }

            PID pid;
            try
            {
                pid = ResolvePid(targetActorId);
            }
            catch (InvalidOperationException)
            {
                var messageType = envelope.Headers.GetValueOrDefault(AgctorMessageHeaders.MessageType, envelope.PayloadType());
                DeadLetter?.Invoke(this, new DeadLetterEventArgs(
                    envelope.Id,
                    senderId,
                    targetActorId,
                    messageType,
                    envelope.Payload,
                    "target-actor-not-found"));
                return Task.CompletedTask;
            }

            // Determine if correlation present
            bool hasCorr = envelope.Metadata.TryGetValue(AgctorMessageHeaders.CorrelationId, out var _) || (envelope.Headers != null && envelope.Headers.TryGetValue(AgctorMessageHeaders.CorrelationId, out var _));

            // Ensure CorrelationId is present in Metadata so that the reply proxy can match responses even if caller only set header
            if (hasCorr && !envelope.Metadata.ContainsKey(AgctorMessageHeaders.CorrelationId))
            {
                if (envelope.Headers != null && envelope.Headers.TryGetValue(AgctorMessageHeaders.CorrelationId, out var hdrCorr))
                {
                    envelope.Metadata[AgctorMessageHeaders.CorrelationId] = hdrCorr;
                }
            }

            // Always use Request so that the reply (even to self) is seen by the proxy and any awaiting requester.
            _root.Request(pid, envelope, _replyPid);

            string corrVal="-";
            if (hasCorr)
            {
                if (envelope.Metadata.TryGetValue(AgctorMessageHeaders.CorrelationId, out var tmp) && tmp!=null) corrVal=tmp.ToString();
                else if (envelope.Headers!=null && envelope.Headers.TryGetValue(AgctorMessageHeaders.CorrelationId, out var htmp)) corrVal=htmp;
            }
            Console.WriteLine($"[ProtoAdapter] Send-only message {envelope.Headers.GetValueOrDefault(AgctorMessageHeaders.MessageType)} to {targetActorId} corr={corrVal} self={(targetActorId==senderId?"yes":"no")}");

            Interlocked.Increment(ref _totalMessages);
            _actorMsgCount.AddOrUpdate(targetActorId, 1, (_, v) => v + 1);
            MessageSent?.Invoke(this, new MessageSentEventArgs(envelope.Id, senderId, targetActorId, message.GetType().Name));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends a message and waits for a response from the target Proto.Actor.
        /// TODO: Implement Proto.Actor request-response pattern using context.RequestAsync().
        /// </summary>
        public async Task<TResponse> SendMessageAsync<TResponse>(string targetActorId, object message, TimeSpan timeout, string? senderId = null, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) where TResponse : class
        {
            if (!_isInitialized) throw new InvalidOperationException("Runtime not initialized");

            var pid = ResolvePid(targetActorId);

            // Correlation id and TCS like InMemory runtime
            var corrId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<IMessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(corrId, tcs))
                throw new InvalidOperationException("Failed to add pending correlation id");

            // Build envelope
            var hdrs = headers != null ? new Dictionary<string,string>(headers) : new Dictionary<string,string>();
            if (!hdrs.ContainsKey(AgctorMessageHeaders.MessageType))
                hdrs[AgctorMessageHeaders.MessageType] = message is string ? AgctorMessageTypes.Prompt : message.GetType().Name;

            var env = message is IMessageEnvelope envIn
                ? envIn
                : AgctorEnvelopeBuilder.Request(
                    message,
                    senderId ?? "proto-client",
                    targetActorId,
                    corrId,
                    hdrs[AgctorMessageHeaders.MessageType],
                    hdrs);

            if (message is IMessageEnvelope)
            {
                // Inject correlation/timestamp into metadata so the reply can be matched
                env.Metadata[AgctorMessageHeaders.CorrelationId] = corrId;
                if (!env.Metadata.ContainsKey("Timestamp")) env.Metadata["Timestamp"] = DateTimeOffset.UtcNow;
            }

            // Send using Request with explicit sender (_replyPid)
            _root.Request(pid, env, _replyPid);

            Console.WriteLine($"[ProtoAdapter] Request sent -> {targetActorId}. Corr={corrId} Type={env.Headers.GetValueOrDefault(AgctorMessageHeaders.MessageType)} PayloadType={env.Payload.GetType().Name}");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delayTask = Task.Delay(timeout, timeoutCts.Token);
            var completed = await Task.WhenAny(tcs.Task, delayTask);
            if (completed == delayTask)
            {
                _pendingRequests.TryRemove(corrId, out _);
                throw new TimeoutException($"Request to {targetActorId} timed out after {timeout.TotalSeconds}s");
            }

            timeoutCts.Cancel();
            var respEnv = await tcs.Task;

            Console.WriteLine($"[ProtoAdapter] Response received Corr={corrId} PayloadType={respEnv.Payload?.GetType().Name ?? "null"}");

            // If caller expects the entire envelope, return it directly
            if (typeof(IMessageEnvelope).IsAssignableFrom(typeof(TResponse)))
            {
                if (respEnv.Payload is JsonElement je2 && je2.ValueKind==JsonValueKind.String)
                {
                    respEnv = respEnv.WithPayload((je2.GetString() ?? string.Empty).Trim());
                }
                else if (respEnv.Payload is string str)
                {
                    var cleaned = str.Trim().Trim('"');
                    respEnv = respEnv.WithPayload(cleaned);
                }
                Console.WriteLine($"[Debug] Returning envelope payload type={respEnv.Payload.GetType().Name} value='{respEnv.Payload}'");
                return (TResponse)(object)respEnv;
            }

            // Convert payload to expected type like InMemory
            if (typeof(TResponse)==typeof(string))
            {
                if (respEnv.Payload is JsonElement je && je.ValueKind==JsonValueKind.String)
                    return (TResponse)(object)(je.GetString() ?? string.Empty);
                return (TResponse)(object)(respEnv.Payload?.ToString() ?? string.Empty);
            }

            if (respEnv.Payload is TResponse good)
                return good;

            if (typeof(TResponse)==typeof(object))
                return (TResponse)respEnv.Payload!;

            throw new InvalidOperationException($"Unexpected payload type {respEnv.Payload?.GetType().Name}");
        }

        /// <summary>
        /// Stops and removes a Proto.Actor from the runtime.
        /// TODO: Implement Proto.Actor stopping using context.Stop().
        /// </summary>
        public Task StopActorAsync(string actorId, CancellationToken cancellationToken = default)
        {
            if (_pidMap.TryRemove(actorId, out var pid))
            {
                _root.Stop(pid);
                ActorStopped?.Invoke(this, new ActorStoppedEventArgs(actorId, "Unknown"));
            }
            _actorInstances.TryRemove(actorId, out _);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets a list of all active Proto.Actor IDs in the runtime.
        /// TODO: Implement Proto.Actor process registry querying for active PIDs.
        /// </summary>
        public Task<IEnumerable<string>> GetActiveActorIdsAsync(CancellationToken cancellationToken = default)
        {
            IEnumerable<string> ids = _pidMap.Keys;
            return Task.FromResult(ids);
        }

        /// <summary>
        /// Gets Proto.Actor runtime statistics and health information.
        /// TODO: Implement Proto.Actor metrics collection and system monitoring.
        /// </summary>
        public Task<IRuntimeStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            var uptime = DateTimeOffset.UtcNow - _startTime;
            int mailboxTotal=0;
            // Removed mailbox calculation due to visibility
            var avgMailbox=0.0;
            var metrics = new Dictionary<string, object>{
                {"PerActorMessageCount", new Dictionary<string,long>(_actorMsgCount)},
                {"AverageMailboxLength", avgMailbox},
                {"TotalMailboxLength", mailboxTotal}
            };
            var stats=new RuntimeStats(_pidMap.Count,_totalMessages,uptime,avgMailbox,metrics);
            return Task.FromResult<IRuntimeStatistics>(stats);
        }

        /// <summary>
        /// Disposes the Proto.Actor adapter and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            // TODO: Implement proper Proto.Actor resource cleanup
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Requests human input. Not currently supported by the Proto.Actor adapter placeholder.
        /// </summary>
        public Task<string> RequestHumanInputAsync(string requestingAgentId, string prompt, string instructions, CancellationToken cancellationToken = default)
        {
            LogWarning($"RequestHumanInputAsync called on ProtoActorAdapter for agent {requestingAgentId}, but it is not implemented.");
            throw new NotImplementedException("Human input via CLI is not supported by the ProtoActorAdapter at this time. This adapter is a placeholder.");
        }

        // Placeholder for logging
        private void LogWarning(string message)
        {
            Console.WriteLine($"[WARN] ProtoActorAdapter: {message}");
        }

        // Add helper CreateActorInstance method similar to InMemory
        private T CreateActorInstance<T>(string actorId) where T: class, AgctorIActor
        {
            try
            {
                return (T)System.Activator.CreateInstance(typeof(T), actorId)!;
            }
            catch (MissingMethodException ex)
            {
                throw new InvalidOperationException($"Could not create instance of actor type '{typeof(T).Name}'. Ensure it has a public constructor taking a string id.", ex);
            }
        }

        // Inner ProtoActorShell implementation
        private class ProtoActorShell : ProtoActorInterface
        {
            private readonly AgctorIActor _real;
            private readonly ProtoActorAdapter _adapter;
            public ProtoActorShell(AgctorIActor real, ProtoActorAdapter adapter)
            {
                _real = real;
                _adapter = adapter;
            }
            public async Task ReceiveAsync(IContext context)
            {
                if (context.Message is Terminated) return;
                var env = context.Message as IMessageEnvelope ?? new AgctorSDK.Core.Messages.MessageEnvelope(context.Message!);
                var replyObj = await _real.ReceiveAsync(env);
                _adapter.RecordInbound(_real.Id);

                if (context.Sender != null && replyObj == null) return;

                IMessageEnvelope replyEnv;
                if (replyObj is IMessageEnvelope re)
                {
                    // Ensure correlation id present
                    if (!re.Metadata.ContainsKey(AgctorMessageHeaders.CorrelationId) && env.Metadata.TryGetValue(AgctorMessageHeaders.CorrelationId, out var cidVal))
                    {
                        re.Metadata[AgctorMessageHeaders.CorrelationId] = cidVal;
                    }
                    replyEnv = re;
                }
                else
                {
                    // Wrap raw payload in an envelope so correlation id flows back
                    var meta = new Dictionary<string,object>();
                    if (env.Metadata.TryGetValue(AgctorMessageHeaders.CorrelationId, out var cid)) meta[AgctorMessageHeaders.CorrelationId] = cid;
                    meta["Timestamp"] = DateTimeOffset.UtcNow;

                    var hdrs = new Dictionary<string,string>
                    {
                        [AgctorMessageHeaders.SenderId] = _real.Id,
                    };
                    if (env.Headers.GetValueOrDefault(AgctorMessageHeaders.SenderId) is string originalSender)
                    {
                        hdrs[AgctorMessageHeaders.ReceiverId] = originalSender;
                    }
                    hdrs[AgctorMessageHeaders.MessageType] = AgctorMessageTypes.Result;

                    replyEnv = new AgctorSDK.Core.Messages.MessageEnvelope(replyObj, meta, null, hdrs);
                }

                if (replyEnv.Metadata.TryGetValue(AgctorMessageHeaders.CorrelationId, out var corrObj))
                {
                    Console.WriteLine($"[ProtoShell] Actor {_real.Id} responding corr={corrObj}");
                }
                else
                {
                    Console.WriteLine($"[ProtoShell] Actor {_real.Id} responding corr=-");
                }
                context.Respond(replyEnv);
            }
        }

        private void RecordInbound(string actorId)
        {
            Interlocked.Increment(ref _totalMessages);
            _actorMsgCount.AddOrUpdate(actorId,1,(_,v)=>v+1);
        }

        private class RuntimeStats : IRuntimeStatistics
        {
            public RuntimeStats(int active, long total, TimeSpan up, double avgMailbox, IReadOnlyDictionary<string, object> additional)
            {
                ActiveActorCount = active;
                TotalMessagesProcessed = total;
                Uptime = up;
                AverageMailboxLength = avgMailbox;
                AdditionalMetrics = additional;
            }
            public int ActiveActorCount { get; }
            public long TotalMessagesProcessed { get; }
            public double MessagesPerSecond => Uptime.TotalSeconds > 0 ? TotalMessagesProcessed / Uptime.TotalSeconds : 0;
            public double AverageMessageProcessingTime => 0;
            public TimeSpan Uptime { get; }
            public long MemoryUsageBytes => GC.GetTotalMemory(false);
            public IReadOnlyDictionary<string, object> AdditionalMetrics { get; }
            public double AverageMailboxLength { get; }
        }

        private static bool TryParseRemote(string actorId, out string name, out string address)
        {
            var at = actorId.IndexOf('@');
            if (at > 0 && at < actorId.Length - 1)
            {
                name = actorId[..at];
                address = actorId[(at + 1)..];
                return true;
            }
            name = string.Empty; address = string.Empty; return false;
        }
        private PID ResolvePid(string targetActorId)
        {
            if (_pidMap.TryGetValue(targetActorId, out var pid)) return pid;
            if (TryParseRemote(targetActorId, out var name, out var address))
            {
                return PID.FromAddress(address, name);
            }
            throw new InvalidOperationException($"Actor {targetActorId} not found locally or remote spec invalid");
        }

        // Helper method to locate a free TCP port on the loopback adapter.
        private static int GetAvailablePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // Merge extension helpers for dictionaries
    }

    internal static class DictionaryExtensions
    {
        public static void Merge(this IDictionary<string, object> target, IDictionary<string, object> source)
        {
            foreach (var kv in source) target[kv.Key]=kv.Value;
        }
        public static void Merge(this IDictionary<string, string> target, IDictionary<string, string> source)
        {
            foreach (var kv in source) target[kv.Key]=kv.Value;
        }
    }

    // Proxy actor that receives all replies and matches correlation ids
    internal class ReplyProxy : ProtoActorInterface
    {
        private readonly ProtoActorAdapter _adapter;
        public ReplyProxy(ProtoActorAdapter adapter){_adapter=adapter;}
        public Task ReceiveAsync(IContext context)
        {
            if (context.Message is IMessageEnvelope env)
            {
                if (env.Metadata!=null && env.Metadata.TryGetValue(AgctorMessageHeaders.CorrelationId, out var cidObj))
                {
                    string? cid = cidObj as string;
                    if (cid==null && cidObj is JsonElement je && je.ValueKind==JsonValueKind.String)
                    {
                        cid = je.GetString();
                    }
                    if (cid==null) return Task.CompletedTask;
                    if (_adapter._pendingRequests.TryGetValue(cid, out var tcs))
                    {
                        var msgType = env.Headers?.GetValueOrDefault(AgctorMessageHeaders.MessageType);
                        if (msgType==AgctorMessageTypes.Acknowledgment) return Task.CompletedTask; // interim
                        Console.WriteLine($"[ReplyProxy] Corr={cid} passing payload type={env.Payload.GetType().Name}");
                        tcs.TrySetResult(env);
                        _adapter._pendingRequests.TryRemove(cid, out _);
                    }
                }
            }
            return Task.CompletedTask;
        }
    }
} 