namespace AgctorSDK.Core.Sessions
{
    /// <summary>
    /// Controls how session history is reduced into prompt context.
    /// </summary>
    public sealed class SessionMemoryOptions
    {
        public int RecentTurnWindow { get; set; } = 8;
        public int SummaryRefreshTurns { get; set; } = 12;
        public int MaxContextChars { get; set; } = 12000;
    }
}
