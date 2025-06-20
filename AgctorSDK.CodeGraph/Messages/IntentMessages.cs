namespace AgctorSDK.CodeGraph.Messages
{
    /// <summary>
    /// Sent to an IntentDetectionAgent requesting natural-language prompt interpretation.
    /// </summary>
    public sealed record InterpretQueryMessage(string Prompt);

    /// <summary>
    /// Returned by IntentDetectionAgent with structured interpretation.
    /// </summary>
    public sealed record IntentResolvedMessage(Intents.IntentResolution Resolution);
} 