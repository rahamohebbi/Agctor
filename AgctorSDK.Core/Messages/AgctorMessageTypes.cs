namespace AgctorSDK.Core.Messages;

/// <summary>
/// Standard framework-level message type values used in actor envelopes.
/// Domain-specific actors can add their own typed payloads while reusing these
/// values for common protocol responses.
/// </summary>
public static class AgctorMessageTypes
{
    public const string Prompt = "Prompt";
    public const string Command = "Command";
    public const string Request = "Request";
    public const string Response = "Response";
    public const string Result = "Result";
    public const string Acknowledgment = "Acknowledgment";
    public const string Error = "Error";
    public const string ErrorResponse = "ErrorResponse";
}

