using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>
/// Default implementation. Wraps the resolver, focus store, project loader, and entity registry so the
/// in-process pipeline runner and the dashboard playground SSE path share one coref code path.
/// Designed to never throw out of <see cref="PreprocessAsync"/>; any failure degrades to "unchanged
/// message, no active subject" so extraction can still proceed.
/// </summary>
public sealed class ProjectMemoryCoreferenceCoordinator : IProjectMemoryCoreferenceCoordinator
{
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IConversationCoreferenceResolver _resolver;
    private readonly IConversationFocusStore _focusStore;

    public ProjectMemoryCoreferenceCoordinator(
        IProjectLoader loader,
        IEntityRegistry entities,
        IConversationCoreferenceResolver resolver,
        IConversationFocusStore focusStore)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _focusStore = focusStore ?? throw new ArgumentNullException(nameof(focusStore));
    }

    /// <inheritdoc />
    public async Task<CoreferencePreprocessResult> PreprocessAsync(
        string projectRoot,
        string? scenarioId,
        string userMessage,
        string? conversationPrefix,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(userMessage))
        {
            return new CoreferencePreprocessResult
            {
                ResolvedUserMessage = userMessage ?? "",
                Reason = "skipped-empty-input",
                KnownEntities = Array.Empty<KnownEntity>()
            };
        }

        var root = Path.GetFullPath(projectRoot.Trim());
        var entityWorkspace = PersonaScenarioScope.GetEntityWorkspaceRoot(root, scenarioId);
        if (!PersonaScenarioScope.IsUnderProjectRoot(root, entityWorkspace))
        {
            return new CoreferencePreprocessResult
            {
                ResolvedUserMessage = userMessage,
                Reason = "skipped-invalid-scope",
                KnownEntities = Array.Empty<KnownEntity>()
            };
        }

        ConversationFocus? focus = null;
        IReadOnlyList<KnownEntity> known = Array.Empty<KnownEntity>();

        try
        {
            focus = await _focusStore.LoadAsync(root, scenarioId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Focus is best-effort; missing/corrupt focus must never break ingest.
        }

        try
        {
            var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            var discovered = await _entities.DiscoverAsync(ctx, entityWorkspace, cancellationToken).ConfigureAwait(false);
            known = discovered.Select(e => new KnownEntity
            {
                EntityKey = e.EntityKey,
                DisplayName = e.Metadata?.DisplayName ?? e.EntityKey,
                Aliases = (IReadOnlyList<string>?)e.Metadata?.Aliases ?? Array.Empty<string>()
            }).ToList();
        }
        catch
        {
            // Discovery failures degrade gracefully — no whitelist, resolver returns unchanged.
        }

        CoreferenceResolution resolution;
        try
        {
            resolution = await _resolver.ResolveAsync(new CoreferenceRequest
            {
                UserMessage = userMessage,
                ConversationPrefix = conversationPrefix,
                CurrentFocus = focus,
                KnownEntities = known
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Surface the original message; do not block extraction if the resolver itself fails.
            resolution = CoreferenceResolution.Unchanged(userMessage, focus?.EntityKey, "resolver-error");
        }

        var activeSubjectKey = resolution.ActiveSubjectEntityKey ?? focus?.EntityKey;
        var activeSubjectDisplay = LookupDisplay(activeSubjectKey, known) ?? focus?.DisplayName;

        return new CoreferencePreprocessResult
        {
            ResolvedUserMessage = resolution.Changed ? resolution.RewrittenMessage : userMessage,
            Changed = resolution.Changed,
            ActiveSubjectKey = activeSubjectKey,
            ActiveSubjectDisplay = activeSubjectDisplay,
            Reason = resolution.Reason,
            FocusBefore = focus,
            KnownEntities = known
        };
    }

    /// <inheritdoc />
    public async Task PersistFocusFromExtractAsync(
        string projectRoot,
        string? scenarioId,
        string? rawExtractorLlmText,
        string? activeSubjectFromPreprocess,
        IReadOnlyList<KnownEntity> knownEntities,
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return;

        try
        {
            var (slug, source) = ResolveFocusFromExtract(rawExtractorLlmText, activeSubjectFromPreprocess);
            if (string.IsNullOrWhiteSpace(slug))
                return;

            var displayName = LookupDisplay(slug, knownEntities) ?? slug;
            var focus = new ConversationFocus
            {
                EntityKey = slug!.Trim(),
                DisplayName = displayName,
                UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                UpdatedBySessionId = sessionId,
                Source = source
            };
            await _focusStore.SaveAsync(projectRoot, scenarioId, focus, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: never break the pipeline on a persist error.
        }
    }

    /// <summary>
    /// Prefer the most recent <c>profile_fact/name</c> in this turn's extractor output; fall back to the
    /// resolver hint (or previous focus) so a pure pronoun turn still updates the persisted active subject.
    /// </summary>
    internal static (string? Slug, string Source) ResolveFocusFromExtract(string? rawExtract, string? coreferenceSubject)
    {
        if (!string.IsNullOrWhiteSpace(rawExtract)
            && MemoryIntentJson.TryParseBatch(rawExtract, out var batch, out _, out _)
            && batch != null
            && batch.MemoryIntents != null)
        {
            for (var i = batch.MemoryIntents.Count - 1; i >= 0; i--)
            {
                var intent = batch.MemoryIntents[i];
                if (intent == null) continue;
                if (string.Equals(intent.KnowledgeType, "profile_fact", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(intent.Attribute, "name", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(intent.EntityKey))
                {
                    return (intent.EntityKey.Trim(), "extracted");
                }
            }

            foreach (var intent in batch.MemoryIntents)
            {
                if (intent != null && !string.IsNullOrWhiteSpace(intent.EntityKey))
                    return (intent.EntityKey.Trim(), "extracted");
            }
        }

        if (!string.IsNullOrWhiteSpace(coreferenceSubject))
            return (coreferenceSubject!.Trim(), "resolved");
        return (null, "none");
    }

    private static string? LookupDisplay(string? slug, IReadOnlyList<KnownEntity> entities)
    {
        if (string.IsNullOrWhiteSpace(slug) || entities == null) return null;
        foreach (var e in entities)
            if (string.Equals(e.EntityKey, slug, StringComparison.OrdinalIgnoreCase))
                return e.DisplayName;
        return null;
    }
}
