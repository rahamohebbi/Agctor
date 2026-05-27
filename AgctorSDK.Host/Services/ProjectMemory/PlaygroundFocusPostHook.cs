using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Core.Streaming;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>After focus-subject resolution: persist conversation focus, sync SQLite project, notify playground UI.</summary>
public sealed class PlaygroundFocusPostHook
{
    private readonly IConversationFocusStore _focusStore;
    private readonly ISessionStore _sessions;

    public PlaygroundFocusPostHook(IConversationFocusStore focusStore, ISessionStore sessions)
    {
        _focusStore = focusStore;
        _sessions = sessions;
    }

    public async Task<PlaygroundFocusSyncPayload?> ApplyAsync(
        string projectRoot,
        string? scenarioId,
        SessionProject? project,
        string? sessionId,
        string? entityKey,
        string? displayName,
        string source,
        CancellationToken cancellationToken)
    {
        var key = FocusEntityPolicy.NormalizeSlugOrNull(entityKey);
        if (string.IsNullOrWhiteSpace(key) || project == null)
            return null;

        var display = string.IsNullOrWhiteSpace(displayName) ? key : displayName.Trim();
        await _focusStore.SaveAsync(
                projectRoot,
                scenarioId,
                new ConversationFocus
                {
                    EntityKey = key,
                    DisplayName = display,
                    UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                    UpdatedBySessionId = sessionId,
                    Source = source
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(key, project.FocusEntityKey, StringComparison.OrdinalIgnoreCase))
        {
            return new PlaygroundFocusSyncPayload(key, display, UpdatedProject: false);
        }

        var updated = await _sessions.UpdateProjectAsync(
                new SessionProject
                {
                    ProjectId = project.ProjectId,
                    Name = project.Name,
                    ScenarioId = project.ScenarioId,
                    FocusEntityKey = key,
                    FocusDisplayName = display,
                    SettingsJson = project.SettingsJson,
                    CreatedAt = project.CreatedAt,
                    SessionCount = project.SessionCount
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new PlaygroundFocusSyncPayload(
            updated.FocusEntityKey ?? key,
            updated.FocusDisplayName ?? display,
            UpdatedProject: true);
    }
}

public sealed record PlaygroundFocusSyncPayload(string FocusEntityKey, string FocusDisplayName, bool UpdatedProject);

public static class PlaygroundFocusSse
{
    public static AgentStreamEvent FocusUpdated(PlaygroundFocusSyncPayload payload, string agentId) =>
        new()
        {
            Type = "focus_updated",
            AgentId = agentId,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                focusEntityKey = payload.FocusEntityKey,
                focusDisplayName = payload.FocusDisplayName
            })
        };
}
