using System.Reflection;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Implementations;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Tools;

/// <summary>Smoke test: attributed tools are discoverable from the tools assembly (host uses the same scan).</summary>
public sealed class AgctorHostToolDiscoveryTests
{
    [Fact]
    public void Attributed_tool_types_include_project_memory_tools()
    {
        var types = typeof(FileSystemTool).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<AgctorHostToolAttribute>() != null)
            .Select(t => t.GetCustomAttribute<AgctorHostToolAttribute>()!.HttpId)
            .ToList();

        types.Should().Contain("person-memory-context");
        types.Should().Contain("apply-memory-intents");
        types.Should().Contain("file-system");
    }
}
