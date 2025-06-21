using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Adapters;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.IntegrationTests.Tools
{
    [TestClass]
    public class CodeEditorToolIntegrationTests
    {
        private IActorRuntimeAdapter _runtime = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _runtime = new InMemoryActorRuntime();
            _runtime.InitializeAsync(new Dictionary<string, object>()).Wait();
        }

        [TestMethod]
        public async Task CodeEditorTool_WriteInsertReplace_EndToEnd()
        {
            var toolId = "editor-tool-e2e";
            var filePath = Path.GetTempFileName();
            var tool = await _runtime.SpawnActorAsync(toolId, (id) => new CodeEditorTool(id));

            try
            {
                // 1. Write initial content
                var initialContent = "line 1\nline 3";
                var writeRequest = new ToolRequest
                {
                    Operation = "WriteFile",
                    Parameters = new Dictionary<string, object> { { "path", filePath }, { "content", initialContent } }
                };
                var writeResultEnvelope = await tool.ReceiveAsync(new MessageEnvelope(writeRequest, headers: new Dictionary<string, string> { { "ReceiverId", toolId } }));
                Assert.IsInstanceOfType(writeResultEnvelope.Payload, typeof(ToolResult));
                var writeResult = (ToolResult)writeResultEnvelope.Payload;
                Assert.IsTrue(writeResult.IsSuccess);
                Assert.AreEqual(initialContent, await File.ReadAllTextAsync(filePath));

                // 2. Insert a line
                var insertRequest = new ToolRequest
                {
                    Operation = "InsertIntoFile",
                    Parameters = new Dictionary<string, object> { { "path", filePath }, { "content", "line 2" }, { "lineNumber", 1 } }
                };
                var insertResultEnvelope = await tool.ReceiveAsync(new MessageEnvelope(insertRequest, headers: new Dictionary<string, string> { { "ReceiverId", toolId } }));
                Assert.IsInstanceOfType(insertResultEnvelope.Payload, typeof(ToolResult));
                var insertResult = (ToolResult)insertResultEnvelope.Payload;
                Assert.IsTrue(insertResult.IsSuccess);
                var linesAfterInsert = await File.ReadAllLinesAsync(filePath);
                CollectionAssert.AreEqual(new[] { "line 1", "line 2", "line 3" }, linesAfterInsert);

                // 3. Replace a line
                var replaceRequest = new ToolRequest
                {
                    Operation = "ReplaceInFile",
                    Parameters = new Dictionary<string, object> { { "path", filePath }, { "content", "new line 2" }, { "startLine", 1 }, { "endLine", 2 } }
                };
                var replaceResultEnvelope = await tool.ReceiveAsync(new MessageEnvelope(replaceRequest, headers: new Dictionary<string, string> { { "ReceiverId", toolId } }));
                Assert.IsInstanceOfType(replaceResultEnvelope.Payload, typeof(ToolResult));
                var replaceResult = (ToolResult)replaceResultEnvelope.Payload;
                Assert.IsTrue(replaceResult.IsSuccess);
                var linesAfterReplace = await File.ReadAllLinesAsync(filePath);
                CollectionAssert.AreEqual(new[] { "line 1", "new line 2", "line 3" }, linesAfterReplace);
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                await _runtime.StopActorAsync(toolId);
            }
        }
    }
} 