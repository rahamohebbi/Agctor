using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.CodeGraph.Actors
{
    /// <summary>
    /// Base class for all actors that participate in the CodeGraph hierarchy (Solution → Project → File → Class → Method).
    /// It provides a minimal implementation of the <see cref="IActor"/> contract plus hierarchical child-management helpers.
    /// </summary>
    public abstract class CodeGraphActorBase : IActor
    {
        private readonly List<CodeGraphActorBase> _children = new();

        /// <summary>
        /// Friendly display name (solution name, project name, filename, etc.).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Optional file-system path that this actor represents.
        /// Only populated for actors that map to physical files or folders (Solution/Project/File).
        /// </summary>
        public string? PhysicalPath { get; }

        #region IActor implementation
        public string Id { get; }

        public string ActorType => GetType().Name;

        public ActorState State { get; private set; }

        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;

        public virtual Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ChangeState(ActorState.Active, "Initialized");
            return Task.CompletedTask;
        }

        public virtual Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            // Stage-1: no sophisticated message routing – just echo back.
            return Task.FromResult(envelope);
        }

        public virtual Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ChangeState(ActorState.Stopped, "Shutdown");
            return Task.CompletedTask;
        }
        #endregion

        protected CodeGraphActorBase(string name, string? physicalPath = null, string? id = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            PhysicalPath = physicalPath;
            Id = id ?? Guid.NewGuid().ToString();
            State = ActorState.Initializing;
        }

        /// <summary>
        /// Immutable read-only view of this actor's children.
        /// </summary>
        public IReadOnlyList<CodeGraphActorBase> Children => new ReadOnlyCollection<CodeGraphActorBase>(_children);

        /// <summary>
        /// Adds a child actor to this actor.
        /// </summary>
        public void AddChild(CodeGraphActorBase child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            _children.Add(child);
        }

        private void ChangeState(ActorState newState, string? reason = null)
        {
            var previous = State;
            State = newState;
            StateChanged?.Invoke(this, new ActorStateChangedEventArgs(previous, newState, reason));
        }
    }
} 