using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.IntegrationTests.Tools
{
    [TestClass]
    public class FileSystemToolIntegrationTests
    {
        private IActorRuntimeAdapter _runtime = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _runtime = new InMemoryActorRuntime();
            _runtime.InitializeAsync(new Dictionary<string, object>()).Wait();
        }

        [TestMethod]
        public async Task FileSystemTool_WriteAndReadFile_EndToEnd()
        {
            // Arrange
            var toolId = "fs-tool-e2e";
            var filePath = Path.GetTempFileName();
            var fileContent = "Hello from Agctor Integration Test!";
            
            var tool = await _runtime.SpawnActorAsync(toolId, (id) => new FileSystemTool(id));

            var writeRequest = new ToolRequest
            {
                ToolName = "FileSystemTool",
                Operation = "WriteFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", filePath },
                    { "content", fileContent }
                }
            };

            var readRequest = new ToolRequest
            {
                ToolName = "FileSystemTool",
                Operation = "ReadFile",
                Parameters = new Dictionary<string, object>
                {
                    { "path", filePath }
                }
            };

            try
            {
                // Act: Write the file
                var writeEnvelope = new MessageEnvelope(writeRequest, headers: new Dictionary<string, string>{ { "ReceiverId", toolId } });
                var writeResultEnvelope = await tool.ReceiveAsync(writeEnvelope);
                Assert.IsInstanceOfType(writeResultEnvelope.Payload, typeof(ToolResult));
                var writeResult = (ToolResult)writeResultEnvelope.Payload;

                // Assert: Write was successful
                Assert.IsTrue(writeResult.IsSuccess);

                // Act: Read the file
                var readEnvelope = new MessageEnvelope(readRequest, headers: new Dictionary<string, string>{ { "ReceiverId", toolId } });
                var readResultEnvelope = await tool.ReceiveAsync(readEnvelope);
                Assert.IsInstanceOfType(readResultEnvelope.Payload, typeof(ToolResult));
                var readResult = (ToolResult)readResultEnvelope.Payload;

                // Assert: Read was successful and content matches
                Assert.IsTrue(readResult.IsSuccess);
                Assert.AreEqual(fileContent, readResult.Output);
            }
            finally
            {
                // Clean up the temporary file
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                await _runtime.StopActorAsync(toolId);
            }
        }
    }
} 