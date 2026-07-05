namespace AgctorSDK.Host.Services;

/// <summary>
/// Machine-local playground chat settings in <c>appsettings.User.json</c> (same pattern as runtime / LLM).
/// </summary>
public interface IPlaygroundChatSettingsService
{
    int GetMaxConversationTurns();

    PlaygroundChatSettingsDto GetSettings();

    Task<PlaygroundChatSettingsDto> SaveAsync(PlaygroundChatSettingsUpdateDto update, CancellationToken cancellationToken = default);
}

public sealed class PlaygroundChatSettingsDto
{
    public int MaxConversationTurns { get; init; }
    public int MinMaxConversationTurns { get; init; }
    public int MaxMaxConversationTurns { get; init; }
}

public sealed class PlaygroundChatSettingsUpdateDto
{
    public int MaxConversationTurns { get; set; }
}
