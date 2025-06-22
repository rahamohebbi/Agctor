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

namespace AgctorSDK.Core.Adapters
{
    /// <summary>
    /// Proto.Actor runtime adapter implementation.
    /// This adapter provides integration with the Proto.Actor high-performance actor framework.
    /// Currently contains placeholder implementations that will be developed in future iterations.
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
                var remoteConfig = GrpcNetRemoteConfig.BindTo(host!, port);
                _remote = new GrpcNetRemote(_system, remoteConfig);
                await _remote.StartAsync();
            }

            _root = _system.Root;
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
            var pid = ResolvePid(targetActorId);
            IMessageEnvelope envelope = message as IMessageEnvelope ?? new AgctorSDK.Core.Messages.MessageEnvelope(message);
            _root.Send(pid, envelope);
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
            IMessageEnvelope envelope = message as IMessageEnvelope ?? new AgctorSDK.Core.Messages.MessageEnvelope(message);
            var response = await _root.RequestAsync<TResponse>(pid, envelope, timeout);
            Interlocked.Increment(ref _totalMessages);
            _actorMsgCount.AddOrUpdate(targetActorId, 1, (_, v) => v + 1);
            MessageSent?.Invoke(this, new MessageSentEventArgs(envelope.Id, senderId, targetActorId, message.GetType().Name));
            return response;
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
                var reply = await _real.ReceiveAsync(env);
                _adapter.RecordInbound(_real.Id);
                if (context.Sender != null && reply!=null)
                {
                    context.Respond(reply);
                }
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
    }
} 