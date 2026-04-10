using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.Tools.Implementations;

/// <summary>
/// Schema-scoped file operations for portable projects (PRD §19); uses <see cref="ProjectMemoryServiceAccessor"/>.
/// </summary>
public sealed class ProjectMemoryTool : BaseActor, IToolActor
{
    public ProjectMemoryTool(string id) : base(id, "ProjectMemoryTool")
    {
    }

    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is ToolRequest request)
        {
            var r = await Handle(request).ConfigureAwait(false);
            return new MessageEnvelope(r);
        }

        return new MessageEnvelope(new ToolResult { IsSuccess = false, Error = "Expected ToolRequest." });
    }

    public async Task<ToolResult> Handle(ToolRequest request)
    {
        try
        {
            var root = ProjectMemoryServiceAccessor.GetRequiredService<IOptions<ProjectMemoryAgentOptions>>().Value.ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
                return Fail("Agctor:ProjectMemory:ProjectRoot not set.");

            var loader = ProjectMemoryServiceAccessor.GetRequiredService<IProjectLoader>();
            var ctx = await loader.LoadAsync(root).ConfigureAwait(false);
            var agentId = GetStr(request.Parameters, "agentId") ?? "person-extractor";
            var spec = ctx.AgentSpecs.FirstOrDefault(a => a.Id == agentId)
                       ?? throw new InvalidOperationException($"Agent spec '{agentId}' not found.");

            var ops = ProjectMemoryServiceAccessor.GetRequiredService<ProjectMemoryOperations>();

            return request.Operation switch
            {
                "read_document" => Ok(await ops.ReadDocumentAsync(spec, root, GetStr(request.Parameters, "path") ?? "", CancellationToken.None).ConfigureAwait(false)),
                "write_document" => await Write(ops, spec, root, request.Parameters).ConfigureAwait(false),
                "load_schema" => Ok(await ops.LoadSchemaAsync(spec, root, GetStr(request.Parameters, "path") ?? "", CancellationToken.None).ConfigureAwait(false)),
                "search_entities" => Ok(System.Text.Json.JsonSerializer.Serialize(
                    await ops.SearchEntitiesAsync(root, GetStr(request.Parameters, "query"), CancellationToken.None).ConfigureAwait(false))),
                _ => Fail($"Unknown operation: {request.Operation}")
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static async Task<ToolResult> Write(ProjectMemoryOperations ops, AgentDefinitionSpec spec, string root, IDictionary<string, object> p)
    {
        var path = GetStr(p, "path") ?? "";
        var content = GetStr(p, "content") ?? "";
        await ops.WriteDocumentAsync(spec, root, path, content, CancellationToken.None).ConfigureAwait(false);
        return Ok("written");
    }

    private static ToolResult Ok(string output) => new() { IsSuccess = true, Output = output };
    private static ToolResult Fail(string err) => new() { IsSuccess = false, Error = err };

    private static string? GetStr(IDictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? v?.ToString() : null;
}
