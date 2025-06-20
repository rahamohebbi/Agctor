namespace AgctorSDK.CodeGraph.Intents
{
    /// <summary>
    /// Supported query intents for the CodeGraph search system.
    /// Add new kinds as capabilities expand.
    /// </summary>
    public enum IntentKind
    {
        Unknown,
        ListClasses,
        ListFiles,
        ListMethods,
        CountLinesClass,
        CountLinesFile,
        SemanticSearch,
        GetMethodSource,
        GetClassSource
    }
} 