using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.CodeGraph.Snapshots;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.CodeGraph.Agents
{
    public sealed class TestPlannerAgent : Agent
    {
        private readonly string _solutionDir;
        public TestPlannerAgent(string id, string solutionDirectory) : base(id)
        {
            _solutionDir = solutionDirectory;
        }

        public override Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is PlanTestsMessage msg)
            {
                var tasks = BuildPlan(msg.Diff);
                return Task.FromResult(envelope.WithPayload(new TestPlanResult(tasks)));
            }
            return base.ReceiveAsync(envelope, cancellationToken);
        }

        private IReadOnlyCollection<TestTask> BuildPlan(SnapshotDiffResult diff)
        {
            var list = new List<TestTask>();
            foreach (var methodFqn in diff.AddedMethods)
            {
                // methodFqn format Class.Method
                var parts = methodFqn.Split('.');
                if (parts.Length != 2) continue;
                var cls = parts[0];
                var method = parts[1];
                var testProjPath = Path.Combine(_solutionDir, "AgctorSDK.Core.Tests");
                var fileName = cls + "Tests.cs";
                var testFile = Path.Combine(testProjPath, fileName);
                list.Add(new TestTask(cls, method, "", testProjPath, testFile));
            }
            return list;
        }
    }
} 