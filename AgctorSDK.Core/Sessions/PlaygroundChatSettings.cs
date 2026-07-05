namespace AgctorSDK.Core.Sessions;

/// <summary>Bounds and defaults for playground chat transcript context (PRD-013).</summary>
public static class PlaygroundChatSettings
{
    public const int DefaultMaxConversationTurns = 25;
    public const int MinMaxConversationTurns = 1;
    public const int MaxMaxConversationTurns = 100;

    /// <summary><c>Agctor:ProjectMemory:MaxConversationTurns</c> in appsettings.</summary>
    public const string ConfigKey = "Agctor:ProjectMemory:MaxConversationTurns";

    public static int Resolve(int? configured) =>
        Clamp(configured ?? DefaultMaxConversationTurns);

    public static int Clamp(int value) =>
        Math.Clamp(value, MinMaxConversationTurns, MaxMaxConversationTurns);
}
