namespace AgctorSDK.Core.Tools.Models
{
    public class ToolResult
    {
        public bool IsSuccess { get; set; }
        public object Output { get; set; }
        public string Error { get; set; }
        
        public ToolResult()
        {
            IsSuccess = false;
            Output = string.Empty;
            Error = string.Empty;
        }
    }
} 