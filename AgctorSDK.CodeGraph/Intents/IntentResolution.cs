using System.Collections.Generic;

namespace AgctorSDK.CodeGraph.Intents
{
    /// <summary>
    /// Result of intent resolution. If <see cref="Kind"/> is Unknown, the prompt was not understood.
    /// </summary>
    public record IntentResolution(IntentKind Kind, IDictionary<string, string>? Slots)
    {
        public static readonly IntentResolution Unresolved = new(IntentKind.Unknown, null);
        public bool IsSuccess => Kind != IntentKind.Unknown;
    }
} 