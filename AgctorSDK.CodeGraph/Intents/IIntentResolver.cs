using System.Collections.Generic;

namespace AgctorSDK.CodeGraph.Intents
{
    /// <summary>
    /// Defines a component that can turn a natural-language search prompt into a structured intent.
    /// Multiple resolvers can be chained (LLM, regex, heuristics) – the first successful match wins.
    /// </summary>
    public interface IIntentResolver
    {
        IntentResolution Resolve(string prompt);
    }
} 