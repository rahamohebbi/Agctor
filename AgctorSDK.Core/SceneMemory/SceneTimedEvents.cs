using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.SceneMemory;

/// <summary>Shot/take-local, end-exclusive ticks. A rational rate preserves frame and sample positions.</summary>
public sealed record SceneTimeRange(long Start, long End, int RateNumerator = 1000, int RateDenominator = 1)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal StartSeconds => RateNumerator <= 0 ? 0 : (decimal)Start * RateDenominator / RateNumerator;
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal EndSeconds => RateNumerator <= 0 ? 0 : (decimal)End * RateDenominator / RateNumerator;
    public void Validate()
    {
        if (Start < 0 || End <= Start || RateNumerator is < 1 or > 1000000000 || RateDenominator is < 1 or > 1000000)
            throw new ArgumentException("Use a nonempty time range with a positive clock rate.");
        if (EndSeconds > 3600) throw new ArgumentException("Event timing must fit within one hour.");
    }
}

/// <summary>Planned events belong to a shot; confirmed observations belong to a specific take.</summary>
public sealed record SceneTimedEvent(string Id, string Kind, string Description, SceneTimeRange Timing,
    IReadOnlyList<string> EntityIds, string Status = "planned", string? TakeId = null,
    string? Dialogue = null, string? Before = null, string? After = null,
    IReadOnlyList<string>? RelatedEventIds = null, bool Protected = false);
public sealed record SceneTimedEventQuery(string? Text = null, string? ShotId = null, string? EntityId = null,
    string? Kind = null, string? Status = null, string? TakeId = null, SceneTimeRange? Overlap = null,
    int Offset = 0, int Limit = 50, string? ExpectedRevision = null);
public sealed record SceneTimedEventMatch(string ShotId, string ShotName, int ShotOrder, SceneTimedEvent Event);
public sealed record SceneTimedEventResult(string WorkspaceId, string Revision, int Total,
    IReadOnlyList<SceneTimedEventMatch> Matches);

public static class SceneTimedEvents
{
    public static readonly string[] Kinds = { "action", "dialogue", "sound", "music", "camera" };
    public static void Validate(SceneWorkspace workspace, ScenePromptRecord shot, SceneTimedEvent item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 200 || !Kinds.Contains(item.Kind)
            || string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 2000 || item.Timing == null
            || item.EntityIds == null || item.EntityIds.Count > 20 || item.EntityIds.Distinct().Count() != item.EntityIds.Count
            || item.Status is not ("planned" or "confirmed") || item.Dialogue?.Length > 4000
            || item.Before?.Length > 2000 || item.After?.Length > 2000 || item.TakeId?.Length > 200
            || (item.Status == "confirmed" ? string.IsNullOrWhiteSpace(item.TakeId) : item.TakeId != null))
            throw new ArgumentException("Provide a valid event, participants, and planned or take-specific confirmed timing.");
        item.Timing.Validate();
        var known = (shot.DesignSnapshots?.Select(e => e.Id) ?? shot.KnownEntityIds
            ?? workspace.Entities.Where(e => e.CreatedAtSequence == null || e.CreatedAtSequence <= shot.LastSequence).Select(e => e.Id)).ToHashSet();
        if (item.EntityIds.Any(id => !known.Contains(id))) throw new ArgumentException("An event participant is not known in this shot.");
        var duration = SceneShotDuration.Resolve(shot.DurationSeconds, shot.Prompt);
        if (item.Status == "planned" && duration.HasValue && item.Timing.EndSeconds > (decimal)duration.Value)
            throw new ArgumentException("The planned event extends beyond the shot duration.");
        var related = item.RelatedEventIds ?? Array.Empty<string>();
        if (related.Count > 20 || related.Distinct().Count() != related.Count || related.Any(id => id == item.Id ||
            !(shot.TimedEvents ?? Array.Empty<SceneTimedEvent>()).Any(e => e.Id == id && e.TakeId == item.TakeId)))
            throw new ArgumentException("Related events must exist in the same shot and timing source.");
    }

    public static SceneWorkspace Save(SceneWorkspace workspace, string shotId, string revision, SceneTimedEvent item)
    {
        CheckRevision(workspace, revision);
        var shot = Shot(workspace, shotId);
        Validate(workspace, shot, item);
        var events = shot.TimedEvents ?? Array.Empty<SceneTimedEvent>();
        var old = events.SingleOrDefault(e => e.Id == item.Id);
        if (old != null && (old.TakeId != item.TakeId || old.Status != item.Status))
            throw new ArgumentException("An event timing source is fixed. Add a separate event to keep the plan and each take observation.");
        if (old == null && events.Count >= 500) throw new ArgumentException("A shot supports up to 500 timed events.");
        return workspace with { Prompts = workspace.Prompts!.Select(p => p.Id != shotId ? p : p with {
            TimedEvents = events.Where(e => e.Id != item.Id).Append(item).OrderBy(e => e.Timing.StartSeconds).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray()
        }).ToArray() };
    }

    public static SceneWorkspace Delete(SceneWorkspace workspace, string shotId, string eventId, string revision)
    {
        CheckRevision(workspace, revision);
        var shot = Shot(workspace, shotId);
        if (shot.TimedEvents?.Any(e => e.Id == eventId) != true) throw new KeyNotFoundException("Timed event not found.");
        return workspace with { Prompts = workspace.Prompts!.Select(p => p.Id != shotId ? p : p with {
            TimedEvents = p.TimedEvents!.Where(e => e.Id != eventId).Select(e => e with {
                RelatedEventIds = e.RelatedEventIds?.Where(id => id != eventId).ToArray()
            }).ToArray()
        }).ToArray() };
    }

    public static SceneTimedEventResult Query(SceneWorkspace workspace, SceneTimedEventQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Text?.Length > 2000 || query.ShotId?.Length > 200 || query.EntityId?.Length > 200 || query.TakeId?.Length > 200
            || query.Offset < 0 || query.Limit is < 1 or > 200 || (query.Kind != null && !Kinds.Contains(query.Kind))
            || (query.Status != null && query.Status is not ("planned" or "confirmed")))
            throw new ArgumentException("Use valid event filters and a page size from 1 to 200.");
        if (query.ExpectedRevision != null) CheckRevision(workspace, query.ExpectedRevision);
        query.Overlap?.Validate();
        if (query.ShotId != null) Shot(workspace, query.ShotId);
        var names = workspace.Entities.ToDictionary(e => e.Id, e => e.Name);
        var words = (query.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var matches = (workspace.Prompts ?? Array.Empty<ScenePromptRecord>()).SelectMany((p, i) =>
            (p.TimedEvents ?? Array.Empty<SceneTimedEvent>()).Select(e => new SceneTimedEventMatch(p.Id, p.Label, i + 1, e)))
            .Where(m => (query.ShotId == null || m.ShotId == query.ShotId)
                && (query.EntityId == null || m.Event.EntityIds.Contains(query.EntityId))
                && (query.Kind == null || m.Event.Kind == query.Kind) && (query.Status == null || m.Event.Status == query.Status)
                && (query.TakeId == null || m.Event.TakeId == query.TakeId)
                && (query.Overlap == null || m.Event.Timing.StartSeconds < query.Overlap.EndSeconds && m.Event.Timing.EndSeconds > query.Overlap.StartSeconds))
            .Where(m => { var text = string.Join(" ", new[] { m.ShotName, m.Event.Description, m.Event.Dialogue, m.Event.Before, m.Event.After }
                .Concat(m.Event.EntityIds.Select(id => names.GetValueOrDefault(id, id))));
                return words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)); })
            .OrderBy(m => m.ShotOrder).ThenBy(m => m.Event.Timing.StartSeconds).ThenBy(m => m.Event.Id, StringComparer.Ordinal).ToArray();
        return new(workspace.Id, ScenePromptReducer.Revision(workspace), matches.Length, matches.Skip(query.Offset).Take(query.Limit).ToArray());
    }

    private static ScenePromptRecord Shot(SceneWorkspace workspace, string id) => workspace.Prompts?.SingleOrDefault(p => p.Id == id)
        ?? throw new KeyNotFoundException("Shot not found in this story.");
    private static void CheckRevision(SceneWorkspace workspace, string revision)
    {
        if (string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("Refresh Scene Memory before saving or paging.");
        if (ScenePromptReducer.Revision(workspace) != revision)
            throw new InvalidOperationException("Scene Memory changed. Refresh and review before continuing.");
    }
}
