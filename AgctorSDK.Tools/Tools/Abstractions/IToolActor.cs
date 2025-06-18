using AgctorSDK.Core.Interfaces;
using System.Threading.Tasks;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Tools.Abstractions
{
    public interface IToolActor : IAgent {
        Task<ToolResult> Handle(ToolRequest request);
    }
} 