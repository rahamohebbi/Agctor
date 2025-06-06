using System.Collections.Generic;

namespace AgctorSDK.Core.Tools.Models
{
    public class ToolRequest
    {
        public string ToolName { get; set; }
        public string Operation { get; set; }
        public IDictionary<string, object> Parameters { get; set; }

        public ToolRequest()
        {
            Parameters = new Dictionary<string, object>();
        }
    }
} 