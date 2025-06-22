using System;
using System.Threading.Tasks;
using AgctorSDK.Core.Adapters;
using Xunit;

namespace AgctorSDK.Core.IntegrationTests
{
    public class ProtoRemoteIntegrationTests
    {
        [Fact(Timeout = 10000)]
        public async Task RemoteAdapters_CanExchangeMessages()
        {
            // Arrange – start server adapter (port 12000) and spawn Echo actor
            var serverConfig = new System.Collections.Generic.Dictionary<string, object>
            {
                {"remoteHost", "127.0.0.1"},
                {"remotePort", 12000}
            };
            using var serverAdapter = new ProtoActorAdapter();
            await serverAdapter.InitializeAsync(serverConfig);
            await serverAdapter.SpawnActorAsync<AgctorSDK.Core.Agents.EchoAgent>("echo1");

            // Start client adapter (port 12001)
            var clientConfig = new System.Collections.Generic.Dictionary<string, object>
            {
                {"remoteHost", "127.0.0.1"},
                {"remotePort", 12001}
            };
            using var clientAdapter = new ProtoActorAdapter();
            await clientAdapter.InitializeAsync(clientConfig);

            // Act – send a fire-and-forget message to remote Echo actor
            await clientAdapter.SendMessageAsync("echo1@127.0.0.1:12000", "ping");

            // Give the remote system a moment to process
            await Task.Delay(300);

            var stats = await serverAdapter.GetStatisticsAsync();

            // Assert – server has seen at least one message
            Assert.True(stats.TotalMessagesProcessed >= 1, "Server did not record message receipt");
        }

        [Fact(Timeout=10000)]
        public async Task RemoteAdapters_RequestResponse_Works()
        {
            var serverCfg=new System.Collections.Generic.Dictionary<string,object>{{"remoteHost","127.0.0.1"},{"remotePort",13000}};
            using var server=new ProtoActorAdapter();
            await server.InitializeAsync(serverCfg);
            await server.SpawnActorAsync<AgctorSDK.Core.Agents.EchoAgent>("echo2");
            var clientCfg=new System.Collections.Generic.Dictionary<string,object>{{"remoteHost","127.0.0.1"},{"remotePort",13001}};
            using var client=new ProtoActorAdapter();
            await client.InitializeAsync(clientCfg);
            var response=await client.SendMessageAsync<AgctorSDK.Core.Interfaces.IMessageEnvelope>("echo2@127.0.0.1:13000",new AgctorSDK.Core.Messages.MessageEnvelope("hello"),TimeSpan.FromSeconds(3));
            Assert.Equal("hello",response.Payload);
        }

        [Fact(Timeout=15000)]
        public async Task ClusterIdentity_ResolvesAcrossNodes()
        {
            // ... existing code ...
        }
    }
} 