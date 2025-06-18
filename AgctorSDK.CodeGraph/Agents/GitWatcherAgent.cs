using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.CodeGraph.Snapshots;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Utils;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.CodeGraph.Agents
{
    /// <summary>
    /// Minimal agent that can create snapshots on demand (Stage-7 foundation).
    /// </summary>
    public sealed class GitWatcherAgent : Agent
    {
        private readonly string _repoPath;
        private readonly CodeGraph.Actors.CodeGraphActorBase _graphRoot;
        private readonly Analyzers.AnalyzerRegistry _registry;

        public GitWatcherAgent(string id, string repoPath, CodeGraph.Actors.CodeGraphActorBase root, Analyzers.AnalyzerRegistry registry)
            : base(id)
        {
            _repoPath = repoPath;
            _graphRoot = root;
            _registry = registry;
        }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            switch (envelope.Payload)
            {
                case CreateSnapshotMessage msg:
                    var path = await SnapshotService.SaveSnapshotAsync(_graphRoot, _repoPath, msg.CommitSha);
                    return envelope.WithPayload(new SnapshotCreatedMessage(msg.CommitSha, path));
                default:
                    return await base.ReceiveAsync(envelope, cancellationToken);
            }
        }
    }
} 