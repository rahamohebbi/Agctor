using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Completion;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Agents
{
    [TestClass]
    public class CompletionAgentTests
    {
        [TestMethod]
        public async Task ReceiveAsync_ReturnsGeneratorText()
        {
            var generator = new ScriptedGenerator(_ => new TextGenerationResult(true, "a kind note", "script"));
            var agent = new CompletionAgent("writer", generator);
            await agent.InitializeAsync();

            var reply = await agent.ReceiveAsync(new MessageEnvelope(new CompletionRequest("say something")));

            var result = (TextGenerationResult)reply.Payload;
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("a kind note", result.Text);
            Assert.AreEqual("script", result.Source);
        }

        [TestMethod]
        public async Task ReceiveAsync_UsesFallbackWhenGeneratorFails()
        {
            var generator = new ScriptedGenerator(_ => new TextGenerationResult(false, "", "script", "down"));
            var agent = new CompletionAgent("writer", generator);
            await agent.InitializeAsync();

            var reply = await agent.ReceiveAsync(new MessageEnvelope(
                new CompletionRequest("rewrite", FallbackText: "Ask about their day.")));

            var result = (TextGenerationResult)reply.Payload;
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("Ask about their day.", result.Text);
            Assert.AreEqual("fallback", result.Source);
            Assert.AreEqual(ActorState.Active, agent.State);
        }

        [TestMethod]
        public async Task Runtime_RequestResponse_ReturnsCompletion()
        {
            var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>());
            await runtime.SpawnActorAsync("writer", id => new CompletionAgent(id, new ScriptedGenerator(
                _ => new TextGenerationResult(true, "postcard ready", "script"))));

            var result = await runtime.SendMessageAsync<TextGenerationResult>(
                "writer",
                new CompletionRequest("write a postcard"),
                System.TimeSpan.FromSeconds(5));

            Assert.AreEqual("postcard ready", result.Text);
            await runtime.ShutdownAsync();
        }

        private sealed class ScriptedGenerator : ITextGenerator
        {
            private readonly System.Func<TextGenerationRequest, TextGenerationResult> _reply;

            public ScriptedGenerator(System.Func<TextGenerationRequest, TextGenerationResult> reply)
            {
                _reply = reply;
            }

            public string Name => "script";

            public Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_reply(request));
            }
        }
    }
}
