using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Tools;

/// <summary>
/// A tool is an <see cref="IActor"/> that agents invoke for side effects or computation.
/// Tools are not <see cref="IAgent"/> — they do not spawn child agents or participate in agent hierarchy.
/// </summary>
public interface IToolActor : IActor
{
    Task<ToolResult> Handle(ToolRequest request);
}
