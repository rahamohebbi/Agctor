using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Tools
{
    public interface IToolActor : IActor
    {
        Task<ToolResult> Handle(ToolRequest request);
    }
} 