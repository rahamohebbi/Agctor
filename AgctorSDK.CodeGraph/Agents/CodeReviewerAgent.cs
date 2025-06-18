using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Llm;
using AgctorSDK.CodeGraph.Messages;
using AgctorSDK.CodeGraph.Services;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.CodeGraph.Agents
{
    public sealed class CodeReviewerAgent : Agent
    {
        private readonly ILlmClient _llm;
        public CodeReviewerAgent(string id, ILlmClient llm) : base(id) { _llm = llm; }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is ReviewCommitMessage review)
            {
                var textDiff = DiffFormatterService.Format(review.Diff);
                var prompt = $"You are a senior engineer. Provide a concise code review for the following diff:\n{textDiff}\nRespond with pros, cons, and a score between 0 and 10.";
                var response = await _llm.CompleteAsync(prompt);
                var result = new CodeReviewResult(response, new List<FileComment>(), ComputeScore(review.Diff));
                return envelope.WithPayload(result);
            }
            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        private static int ComputeScore(Snapshots.SnapshotDiffResult diff)
        {
            int score = 10;
            if (diff.AddedMethods.Count > 0 && diff.AddedMethods.Count > diff.RemovedMethods.Count)
                score -= 1; // simplistic
            return score;
        }
    }
} 