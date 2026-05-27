using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Coref;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Processing;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Resolution.Bridge;
using AgctorSDK.Core.ProjectMemory.Resolution.Review;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Core.ProjectMemory.Orchestration;

/// <summary>
/// Code-first orchestrator: chains person-extractor → routing/projection → person-query without actor envelopes (same file effects as the dedicated agents).
/// Heavy steps delegate to <see cref="ProjectMemoryQueryService"/> and <see cref="ProjectMemoryIngestService"/> to keep this class a thinner coordinator.
/// </summary>
public sealed class ProjectMemoryPipelineRunner : IProjectMemoryPipelineRunner
{
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IMemoryIntentProcessor _processor;
    private readonly IDocumentProjectionService _projection;
    private readonly ProjectMemoryOperations _ops;
    private readonly IProjectMemoryLlmClient _llm;
    // Search + profile snippets + query prompt + LLM call.
    private readonly ProjectMemoryQueryService _queryService;
    // Raw JSON ingest, generic inbox proposals, and confirm/reject handling.
    private readonly ProjectMemoryIngestService _ingestService;
    private readonly MentionObservationPublisher? _mentions;
    private readonly IGenericInboxStore _genericInbox;
    private readonly IConfirmationIntentClassifier _confirmClassifier;
    private readonly IProjectMemoryCoreferenceCoordinator _coordinator;

    public ProjectMemoryPipelineRunner(
        IProjectLoader loader,
        IEntityRegistry entities,
        IMemoryIntentProcessor processor,
        IDocumentProjectionService projection,
        ProjectMemoryOperations ops,
        IProjectMemoryLlmClient llm,
        IGenericInboxStore genericInbox,
        IConfirmationIntentClassifier? confirmClassifier = null,
        IGenericInboxReplayService? replay = null,
        IConversationCoreferenceResolver? coref = null,
        IFocusSubjectResolver? focusSubject = null,
        IConversationFocusStore? focusStore = null,
        IProjectMemoryCoreferenceCoordinator? coordinator = null,
        MentionObservationPublisher? mentions = null)
    {
        _loader = loader;
        _entities = entities;
        _processor = processor;
        _projection = projection;
        _ops = ops;
        _llm = llm;
        _queryService = new ProjectMemoryQueryService(_ops, _llm);
        _genericInbox = genericInbox;
        _mentions = mentions;
        _confirmClassifier = confirmClassifier ?? new HeuristicConfirmationIntentClassifier();
        // LLM resolver is the canonical default; heuristic only kicks in when no LLM client is wired.
        // The coordinator wraps resolver + focus store + loader/entities so both the pipeline runner
        // and the dashboard playground SSE flow can share one coref code path. Callers may inject a
        // ready-made coordinator (preferred under DI) or pass individual primitives for tests.
        var resolver = coref ?? (_llm != null
            ? new LlmConversationCoreferenceResolver(_llm)
            : (IConversationCoreferenceResolver)new HeuristicConversationCoreferenceResolver());
        var focusSubjectResolver = focusSubject ?? (_llm != null
            ? new FocusSubjectResolver(_llm)
            : (IFocusSubjectResolver)new HeuristicFocusSubjectResolver());
        var focus = focusStore ?? new ConversationFocusStore();
        _coordinator = coordinator
            ?? new ProjectMemoryCoreferenceCoordinator(_loader, _entities, focusSubjectResolver, resolver, focus);
        _ingestService = new ProjectMemoryIngestService(_entities, _processor, _projection, _genericInbox, replay, _mentions);
    }

    /// <inheritdoc />
    public async Task<GenericInboxPersistResult> PersistApprovedGenericFactsAsync(
        string projectRoot,
        string? scenarioId,
        IReadOnlyList<ApprovedGenericFact> approvals,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(projectRoot.Trim());
        if (!Directory.Exists(Path.Combine(root, ".agctor")))
        {
            return new GenericInboxPersistResult
            {
                Errors = new[] { "Project root must contain a .agctor directory." }
            };
        }

        var seg = string.IsNullOrWhiteSpace(scenarioId) ? null : PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
        var result = await _genericInbox.PersistApprovedAsync(root, seg, approvals, cancellationToken).ConfigureAwait(false);
        if (result.Appended > 0 && result.AppendedProposalIds.Count > 0)
        {
            var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            ApprovedFactRoutingLearner.TryLearnAfterPersist(ctx, result, approvals);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<ProjectMemoryIngestResult> IngestFromExtractorOutputAsync(
        string projectRoot,
        string? scenarioId,
        string rawExtractorLlmText,
        CancellationToken cancellationToken = default) =>
        IngestFromExtractorOutputAsync(projectRoot, scenarioId, rawExtractorLlmText, sessionId: null, turnId: null, cancellationToken);

    /// <summary>Overload that tags published mentions with session/turn ids (PRD-018 resolution subsystem).</summary>
    public async Task<ProjectMemoryIngestResult> IngestFromExtractorOutputAsync(
        string projectRoot,
        string? scenarioId,
        string rawExtractorLlmText,
        string? sessionId,
        string? turnId,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(projectRoot.Trim());
        if (!Directory.Exists(Path.Combine(root, ".agctor")))
        {
            return new ProjectMemoryIngestResult
            {
                ParseSuccess = false,
                Summary = "Project root must contain a .agctor directory."
            };
        }

        var entityWorkspace = PersonaScenarioScope.GetEntityWorkspaceRoot(root, scenarioId);
        if (!PersonaScenarioScope.IsUnderProjectRoot(root, entityWorkspace))
        {
            return new ProjectMemoryIngestResult { ParseSuccess = false, Summary = "Invalid scenario scope path." };
        }

        var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
        var work = await _ingestService.IngestFromRawExtractAsync(ctx, rawExtractorLlmText, entityWorkspace, sessionId, turnId, cancellationToken).ConfigureAwait(false);
        if (!work.ParseOk)
        {
            return new ProjectMemoryIngestResult
            {
                ParseSuccess = false,
                ParseSource = work.ParseSource,
                Summary = work.ParseError ?? "Parse failed.",
                OutOfSchemaProposals = work.OutOfSchemaProposals
            };
        }

        if (work.NoIntents)
        {
            return new ProjectMemoryIngestResult
            {
                ParseSuccess = true,
                ParseSource = work.ParseSource,
                WroteAnyFile = false,
                Summary = work.RouteDetail,
                OutOfSchemaProposals = work.OutOfSchemaProposals
            };
        }

        if (work.RouteFatal)
        {
            return new ProjectMemoryIngestResult
            {
                ParseSuccess = true,
                ParseSource = work.ParseSource,
                WroteAnyFile = false,
                Summary = work.RouteDetail,
                OutOfSchemaProposals = work.OutOfSchemaProposals
            };
        }

        return new ProjectMemoryIngestResult
        {
            ParseSuccess = true,
            ParseSource = work.ParseSource,
            WroteAnyFile = work.WriteOk,
            UpdatedFiles = work.UpdatedFiles,
            Summary = work.WriteDetail ?? work.RouteDetail,
            OutOfSchemaProposals = work.OutOfSchemaProposals
        };
    }

    /// <inheritdoc />
    public async Task<ProjectMemoryPipelineResult> RunAsync(ProjectMemoryPipelineRequest request, CancellationToken cancellationToken = default)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();

        var steps = new List<ProjectMemoryPipelineStep>();
        var root = Path.GetFullPath(request.ProjectRoot.Trim());
        if (!Directory.Exists(Path.Combine(root, ".agctor")))
        {
            return Fail(correlationId, "Project root must contain a .agctor directory.", steps);
        }

        var entityWorkspace = PersonaScenarioScope.GetEntityWorkspaceRoot(root, request.ScenarioId);
        if (!PersonaScenarioScope.IsUnderProjectRoot(root, entityWorkspace))
            return Fail(correlationId, "Invalid scenario scope path.", steps);

        var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
        var extractorSpec = ctx.AgentSpecs.FirstOrDefault(a =>
            string.Equals(a.Id, "person-extractor", StringComparison.OrdinalIgnoreCase));
        var querySpec = ctx.AgentSpecs.FirstOrDefault(a =>
            string.Equals(a.Id, "person-query", StringComparison.OrdinalIgnoreCase));

        var mode = request.Mode;
        if (mode != ProjectMemoryPipelineMode.QueryOnly && extractorSpec == null)
            return Fail(correlationId, "person-extractor agent spec missing.", steps);
        if (mode != ProjectMemoryPipelineMode.IngestOnly && querySpec == null)
            return Fail(correlationId, "person-query agent spec missing.", steps);

        var success = true;
        string? rawExtract = null;

        // PRD-019: if this user turn is a pure "yes/no" to the previous out-of-schema prompt,
        // short-circuit the extractor and act on the pending generic-inbox rows directly.
        // Classifier may use the prior assistant prompt + LLM to handle natural phrasing.
        var lastAssistantPrompt = ExtractLastAssistantPrompt(request.ConversationPrefix);
        var confirmSignal = await _confirmClassifier
            .ClassifyAsync(request.UserMessage, lastAssistantPrompt, cancellationToken)
            .ConfigureAwait(false);
        if (confirmSignal != ConfirmationInputDetector.ConfirmationSignal.None)
        {
            var handled = await _ingestService.TryHandleConfirmationAsync(ctx, request, confirmSignal, steps, cancellationToken).ConfigureAwait(false);
            if (handled.Handled)
                return Finish(correlationId, handled.Success, handled.FinalText, steps);
        }

        // PRD-019 Option B + F: persistent coreference focus per scenario. Loaded fresh on every turn so a
        // brand-new browser session in the same scenario inherits the active subject from disk.
        var coref = await _coordinator.PreprocessAsync(
            ctx.ProjectRoot,
            request.ScenarioId,
            request.UserMessage,
            request.ConversationPrefix,
            cancellationToken).ConfigureAwait(false);
        var resolvedUserMessage = coref.ResolvedUserMessage;
        var activeSubjectKey = coref.ActiveSubjectKey;
        var activeSubjectDisplay = coref.ActiveSubjectDisplay;
        var knownEntities = coref.KnownEntities;
        steps.Add(new ProjectMemoryPipelineStep
        {
            Name = "coref",
            Ok = true,
            Detail = $"reason={coref.Reason}; changed={coref.Changed}; activeSubject={activeSubjectKey ?? "<none>"}"
        });

        if (mode != ProjectMemoryPipelineMode.QueryOnly)
        {
            try
            {
                var extractPrompt = ProjectMemoryPromptBuilder.BuildExtractPrompt(
                    extractorSpec!,
                    resolvedUserMessage,
                    request.ConversationPrefix,
                    activeSubjectKey,
                    activeSubjectDisplay);
                rawExtract = await _llm.GenerateAsync(extractPrompt, cancellationToken).ConfigureAwait(false);
                steps.Add(new ProjectMemoryPipelineStep
                {
                    Name = "extract",
                    Ok = true,
                    // Keep full extractor JSON so operators can inspect all intents in the timeline UI.
                    Detail = rawExtract
                });
            }
            catch (Exception ex)
            {
                success = false;
                steps.Add(new ProjectMemoryPipelineStep { Name = "extract", Ok = false, Detail = ex.Message });
                if (mode == ProjectMemoryPipelineMode.IngestOnly)
                {
                    return Finish(correlationId, false, "Extract failed: " + ex.Message, steps);
                }
                // Auto: continue to query without ingest
            }

            if (rawExtract != null &&
                !await TryIngestFromExtractAsync(ctx, rawExtract, steps, entityWorkspace, request.SessionId, request.TurnId, cancellationToken).ConfigureAwait(false))
            {
                success = false;
                if (mode == ProjectMemoryPipelineMode.IngestOnly)
                {
                    var msg = steps.LastOrDefault(s => !s.Ok)?.Detail ?? "Ingest failed.";
                    return Finish(correlationId, false, msg, steps);
                }
            }

            // After ingest, persist the active subject for future turns/sessions.
            await _coordinator.PersistFocusFromExtractAsync(
                ctx.ProjectRoot,
                request.ScenarioId,
                rawExtract,
                activeSubjectKey,
                knownEntities,
                request.SessionId,
                cancellationToken).ConfigureAwait(false);
        }

        if (mode == ProjectMemoryPipelineMode.IngestOnly)
        {
            return Finish(correlationId, success, success ? "Ingest completed." : "Ingest failed; see steps.", steps);
        }

        try
        {
            var answer = await _queryService.RunAsync(querySpec!, root, entityWorkspace, request.UserMessage, request.ConversationPrefix, cancellationToken)
                .ConfigureAwait(false);
            // PRD-018 §5.7 U4: decorate mentions in the final answer with their resolution grade so
            // soft links read as "likely Raha (72%)" instead of the plain "Raha".
            answer = await TryAnnotateWithResolutionAsync(ctx, entityWorkspace, answer, cancellationToken).ConfigureAwait(false);
            steps.Add(new ProjectMemoryPipelineStep { Name = "query", Ok = true, Detail = Truncate(answer, 600) });
            return Finish(correlationId, success, answer, steps);
        }
        catch (Exception ex)
        {
            success = false;
            steps.Add(new ProjectMemoryPipelineStep { Name = "query", Ok = false, Detail = ex.Message });
            return Finish(correlationId, false, "Query failed: " + ex.Message, steps);
        }
    }

    /// <summary>Returns false if ingest path failed (parse, route, or write).</summary>
    private async Task<bool> TryIngestFromExtractAsync(
        LoadedProjectContext ctx,
        string rawExtract,
        List<ProjectMemoryPipelineStep> steps,
        string entityWorkspaceRoot,
        string? sessionId,
        string? turnId,
        CancellationToken cancellationToken)
    {
        var work = await _ingestService.IngestFromRawExtractAsync(ctx, rawExtract, entityWorkspaceRoot, sessionId, turnId, cancellationToken).ConfigureAwait(false);
        if (!work.ParseOk)
        {
            steps.Add(new ProjectMemoryPipelineStep { Name = "parse", Ok = false, Detail = work.ParseError });
            return false;
        }

        if (work.NoIntents)
        {
            steps.Add(new ProjectMemoryPipelineStep { Name = "route", Ok = true, Detail = work.RouteDetail });
            return true;
        }

        if (work.RouteFatal)
        {
            steps.Add(new ProjectMemoryPipelineStep { Name = "route", Ok = false, Detail = work.RouteDetail });
            return false;
        }

        steps.Add(new ProjectMemoryPipelineStep
        {
            Name = "route",
            Ok = true,
            Detail = work.RouteDetail
        });

        var writeOk = work.WriteOk;
        var writeDetail = work.WriteDetail;
        steps.Add(new ProjectMemoryPipelineStep
        {
            Name = "write",
            Ok = writeOk,
            UpdatedFiles = work.UpdatedFiles,
            Detail = writeDetail
        });

        // No file writes is still a successful turn when the runtime has surfaced out-of-schema
        // proposals for the user to confirm (PRD-019) — the pipeline will honor the next "yes".
        if (!writeOk && work.OutOfSchemaProposals.Count > 0)
            return true;

        return writeOk;
    }

    /// <summary>Shared parse → route → projection for ingest (pipeline steps or standalone API).</summary>
    private async Task<RawIngestWork> IngestFromRawExtractAsync(
        LoadedProjectContext ctx,
        string rawExtract,
        string entityWorkspaceRoot,
        string? sessionId,
        string? turnId,
        CancellationToken cancellationToken)
    {
        if (!MemoryIntentJson.TryParseBatch(rawExtract, out var batch, out var parseErr, out var parseSource))
            return new RawIngestWork(false, parseErr, null, false, false, "", false, new List<string>(), null, Array.Empty<OutOfSchemaFactProposal>());

        if (batch!.MemoryIntents.Count == 0)
        {
            return new RawIngestWork(
                true,
                null,
                parseSource,
                true,
                false,
                "No memory intents; skipped write.",
                false,
                new List<string>(),
                null,
                Array.Empty<OutOfSchemaFactProposal>());
        }

        // Discover early so family_role intents can be normalized against known folders before routing.
        var discovered = await _entities.DiscoverAsync(ctx, entityWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        // Snapshot the raw (pre-normalization) surface forms of every family_role intent's participants
        // so when we later bootstrap folders for referenced people we preserve display name casing
        // (e.g. "Ryan" → displayName "Ryan" rather than the slug "ryan").
        var familyReferencedRaw = CollectFamilyReferencedEntityKeys(batch.MemoryIntents);
        var familyNotes = new List<string>();
        FamilyRoleIntentNormalizer.Apply(batch.MemoryIntents, discovered, rawExtract, familyNotes);

        if (batch.MemoryIntents.Count == 0)
        {
            var dropped = familyNotes.Count > 0
                ? "All intents dropped after family_role normalization: " + string.Join("; ", familyNotes)
                : "All intents dropped after family_role normalization.";
            return new RawIngestWork(
                true,
                null,
                parseSource,
                true,
                false,
                dropped,
                false,
                new List<string>(),
                null,
                Array.Empty<OutOfSchemaFactProposal>());
        }

        // PRD-018: every retained intent is a first-class mention observation. Published to the
        // resolution reconciler only when the subsystem is wired in; otherwise this is a no-op.
        if (_mentions != null)
        {
            try
            {
                var scenarioId = TryExtractScenarioId(ctx, entityWorkspaceRoot);
                var mentions = MentionObservationPublisher.FromMemoryIntents(
                    batch.MemoryIntents, scenarioId, sessionId, turnId);
                if (mentions.Count > 0)
                    await _mentions.PublishAsync(ProjectIdFor(ctx), mentions, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Resolution is strictly additive; never fail ingest because of a mention-publish error.
            }
        }

        var routed = _processor.Route(ctx, batch.MemoryIntents, out var routeIssues);
        var oosProposals = OutOfSchemaProposalFactory.FromRouteIssues(routeIssues, ctx.Runtime.OutOfSchema).ToList();
        if (oosProposals.Count > 0)
        {
            // Persist every proposal (immediate + review) so a follow-up "yes" turn can honor it.
            var scen = TryExtractScenarioId(ctx, entityWorkspaceRoot);
            await _genericInbox.AppendPendingAsync(ctx.ProjectRoot, scen, oosProposals, cancellationToken).ConfigureAwait(false);
        }

        var routeErrors = routeIssues.Where(i => i.IsError).ToList();
        if (routeErrors.Count > 0 && routed.Count == 0)
        {
            // When every intent is out-of-schema, treat it as "awaiting user confirmation" rather
            // than a hard failure — the host can still surface the prompt lines to the user.
            if (oosProposals.Count > 0)
            {
                return new RawIngestWork(
                    true,
                    null,
                    parseSource,
                    false,
                    false,
                    "No routable intents; " + oosProposals.Count + " out-of-schema fact(s) await user confirmation.",
                    false,
                    new List<string>(),
                    null,
                    oosProposals);
            }

            return new RawIngestWork(
                true,
                null,
                parseSource,
                false,
                true,
                string.Join("; ", routeErrors.Select(i => i.Message)),
                false,
                new List<string>(),
                null,
                oosProposals);
        }

        var routeDetail = $"Routed {routed.Count} intent(s).";
        if (familyNotes.Count > 0)
            routeDetail = string.Join("; ", familyNotes) + " | " + routeDetail;
        if (routeErrors.Count > 0)
            routeDetail += " Skipped " + routeErrors.Count + " unroutable intent(s): " +
                           string.Join("; ", routeErrors.Select(i => i.Message));
        if (oosProposals.Count > 0)
            routeDetail += " | Out-of-schema: " + oosProposals.Count + " fact(s) surfaced for confirmation (see ingest metadata).";

        var groups = routed.GroupBy(r => r.Original.EntityKey, StringComparer.OrdinalIgnoreCase).ToList();
        var lookup = BuildEntityLookup(discovered);
        var updated = new List<string>();

        // Unknown entityKey (e.g. new "Melody") has no folder yet — discovery skips it and projection needs files on disk.
        var bootstrapped = false;
        foreach (var g in groups)
        {
            if (ResolveEntityRecord(g.Key, discovered, lookup) != null)
                continue;
            var created = await EntityFolderBootstrapper.TryCreateIfMissingAsync(
                    ctx, entityWorkspaceRoot, g.Key, g.ToList(), cancellationToken)
                .ConfigureAwait(false);
            if (created.Count > 0)
            {
                updated.AddRange(created);
                bootstrapped = true;
            }
        }

        // Family edges frequently *reference* people who don't exist yet ("I have a son called Ryan").
        // Bootstrap those too so a mention of a person is enough to give them a first-class folder,
        // using the raw surface form (captured before FamilyRoleIntentNormalizer slugged the values)
        // as the display name hint so the new profile reads correctly.
        foreach (var (entityKey, displayNameHint) in familyReferencedRaw)
        {
            if (ResolveEntityRecord(entityKey, discovered, lookup) != null)
                continue;
            if (groups.Any(g => string.Equals(g.Key, entityKey, StringComparison.OrdinalIgnoreCase)))
                continue; // already bootstrapped in the primary pass
            var synthetic = SyntheticNameIntents(entityKey, displayNameHint);
            var created = await EntityFolderBootstrapper.TryCreateIfMissingAsync(
                    ctx, entityWorkspaceRoot, entityKey, synthetic, cancellationToken)
                .ConfigureAwait(false);
            if (created.Count > 0)
            {
                updated.AddRange(created);
                bootstrapped = true;
            }
        }

        if (bootstrapped)
        {
            discovered = await _entities.DiscoverAsync(ctx, entityWorkspaceRoot, cancellationToken).ConfigureAwait(false);
            lookup = BuildEntityLookup(discovered);
        }

        var unresolved = new List<string>();
        foreach (var g in groups)
        {
            var rec = ResolveEntityRecord(g.Key, discovered, lookup);
            if (rec == null)
            {
                unresolved.Add(g.Key);
                continue;
            }

            var res = await _projection.ApplyAsync(rec, g.ToList(), cancellationToken).ConfigureAwait(false);
            updated.AddRange(res.UpdatedFiles);
        }

        var writeOk = updated.Count > 0;
        var writeDetail = unresolved.Count > 0
            ? "Updated " + updated.Count + " file(s); unresolved entity keys: " + string.Join(", ", unresolved.Distinct(StringComparer.OrdinalIgnoreCase))
            : (updated.Count == 0 ? "No files updated (entities not found?)." : null);
        return new RawIngestWork(true, null, parseSource, false, false, routeDetail, writeOk, updated, writeDetail, oosProposals);
    }

    /// <param name="NoIntents">Parsed OK but <c>memoryIntents</c> empty — pipeline skips write step.</param>
    private sealed record RawIngestWork(
        bool ParseOk,
        string? ParseError,
        string? ParseSource,
        bool NoIntents,
        bool RouteFatal,
        string RouteDetail,
        bool WriteOk,
        List<string> UpdatedFiles,
        string? WriteDetail,
        IReadOnlyList<OutOfSchemaFactProposal> OutOfSchemaProposals);

    /// <summary>
    /// Pulls the most recent <c>Assistant: …</c> line out of <see cref="ProjectMemoryPipelineRequest.ConversationPrefix"/>.
    /// Used by <see cref="IConfirmationIntentClassifier"/> to ground intent ("did this user reply consent to the prior prompt?").
    /// </summary>
    private static string? ExtractLastAssistantPrompt(string? conversationPrefix)
    {
        if (string.IsNullOrWhiteSpace(conversationPrefix))
            return null;

        var lines = conversationPrefix.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("Assistant:", StringComparison.OrdinalIgnoreCase))
                return line["Assistant:".Length..].Trim();
        }

        return null;
    }


    /// <summary>
    /// Pulls referenced people out of <c>family_role</c> intents (both the <c>entityKey</c> side and
    /// the <c>value</c> side) so the pipeline can give them their own folder when they are mentioned
    /// for the first time. Returns (folderSlug, displayNameHint) pairs keyed by slug; the hint is the
    /// raw surface form a user would recognize on disk ("Ryan" rather than "ryan").
    /// </summary>
    private static IReadOnlyList<(string EntityKey, string DisplayNameHint)> CollectFamilyReferencedEntityKeys(
        IReadOnlyList<MemoryIntent> intents)
    {
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (intents == null) return Array.Empty<(string, string)>();
        foreach (var intent in intents)
        {
            if (intent == null) continue;
            if (!string.Equals(intent.KnowledgeType, "family_role", StringComparison.OrdinalIgnoreCase)) continue;

            // Both sides of a family edge may name a new person. entityKey usually resolves to an
            // existing entity but we still handle the "both new" case (e.g. two freshly introduced kids).
            AddFamilyToken(byKey, intent.EntityKey);
            AddFamilyToken(byKey, intent.Value);
        }
        return byKey.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static void AddFamilyToken(Dictionary<string, string> byKey, string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var slug = EntityFolderBootstrapper.SlugFolderSegment(rawToken);
        if (string.IsNullOrEmpty(slug)) return;
        if (byKey.ContainsKey(slug)) return;
        byKey[slug] = rawToken.Trim();
    }

    /// <summary>
    /// Builds a single synthetic <c>profile_fact/name</c> routed intent so
    /// <see cref="EntityFolderBootstrapper.TryCreateIfMissingAsync"/> has a display-name hint to use
    /// when creating the folder for a person who was only referenced as a family edge value.
    /// </summary>
    private static IReadOnlyList<RoutedMemoryIntent> SyntheticNameIntents(string entityKey, string displayNameHint)
    {
        var display = string.IsNullOrWhiteSpace(displayNameHint)
            ? (entityKey.Length == 0 ? entityKey : char.ToUpperInvariant(entityKey[0]) + entityKey[1..])
            : displayNameHint.Trim();
        return new List<RoutedMemoryIntent>
        {
            new()
            {
                Original = new MemoryIntent
                {
                    EntityKey = entityKey,
                    KnowledgeType = "profile_fact",
                    Attribute = "name",
                    Value = display,
                    Confidence = 1.0
                },
                DocumentTypeId = "profile",
                SectionTitle = "Basic Info",
                UpdateMode = "replace_section",
                FileName = "profile.md"
            }
        };
    }

    /// <summary>
    /// Decorates the person-query answer with resolution-grade footnotes ("(soft-linked 72%)", etc.).
    /// Runs on best-effort: an annotator failure leaves the raw answer untouched.
    /// </summary>
    private async Task<string> TryAnnotateWithResolutionAsync(
        LoadedProjectContext ctx,
        string entityWorkspaceRoot,
        string answer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(answer)) return answer;
        try
        {
            var entities = await _entities.DiscoverAsync(ctx, entityWorkspaceRoot, cancellationToken).ConfigureAwait(false);
            var rooted = entities.Select(e => (e.EntityKey, e.Metadata?.DisplayName ?? e.EntityKey, e.RootPath));
            var annotator = ResolutionAnnotator.FromEntities(rooted);
            return annotator.AnnotateInline(answer);
        }
        catch
        {
            return answer;
        }
    }

    /// <summary>
    /// Best-effort scenario id when the entity workspace is <c>scenarios/&lt;id&gt;/</c>; returns null for
    /// project-root ingests.
    /// </summary>
    private static string? TryExtractScenarioId(LoadedProjectContext ctx, string entityWorkspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(entityWorkspaceRoot)) return null;
        var scenariosDir = Path.Combine(ctx.ProjectRoot, "scenarios");
        var full = Path.GetFullPath(entityWorkspaceRoot);
        if (!full.StartsWith(scenariosDir, StringComparison.OrdinalIgnoreCase)) return null;
        var rel = Path.GetRelativePath(scenariosDir, full);
        var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    /// <summary>
    /// Deterministic project id used as the resolution actor key prefix. Using the folder name
    /// keeps the id readable in traces while remaining stable across restarts.
    /// </summary>
    private static string ProjectIdFor(LoadedProjectContext ctx) =>
        string.IsNullOrWhiteSpace(ctx.ProjectRoot)
            ? "default"
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(ctx.ProjectRoot));

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    private static EntityRecord? ResolveEntityRecord(
        string rawEntityKey,
        IReadOnlyList<EntityRecord> discovered,
        Dictionary<string, EntityRecord> lookup)
    {
        if (string.IsNullOrWhiteSpace(rawEntityKey))
            return null;

        if (lookup.TryGetValue(NormalizeEntityToken(rawEntityKey), out var byRaw))
            return byRaw;

        var parts = rawEntityKey.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var p = NormalizeEntityToken(parts[i]);
            if (lookup.TryGetValue(p, out var byPart))
                return byPart;
        }

        var fallback = NormalizeEntityToken(Path.GetFileName(rawEntityKey));
        return lookup.TryGetValue(fallback, out var byFileName) ? byFileName : null;
    }

    private static Dictionary<string, EntityRecord> BuildEntityLookup(IReadOnlyList<EntityRecord> discovered)
    {
        var map = new Dictionary<string, EntityRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in discovered)
        {
            AddLookup(map, e.EntityKey, e);
            AddLookup(map, e.Metadata.DisplayName, e);
            if (e.Metadata.Aliases != null)
            {
                foreach (var a in e.Metadata.Aliases)
                    AddLookup(map, a, e);
            }
            AddLookup(map, Path.GetFileName(e.RootPath), e);
        }

        return map;
    }

    private static void AddLookup(Dictionary<string, EntityRecord> map, string? raw, EntityRecord rec)
    {
        var k = NormalizeEntityToken(raw);
        if (string.IsNullOrEmpty(k))
            return;
        if (!map.ContainsKey(k))
            map[k] = rec;
    }

    private static string NormalizeEntityToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var s = raw.Trim().ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static ProjectMemoryPipelineResult Finish(
        string correlationId,
        bool success,
        string finalText,
        IReadOnlyList<ProjectMemoryPipelineStep> steps) =>
        new()
        {
            CorrelationId = correlationId,
            Success = success,
            FinalText = finalText,
            Steps = steps
        };

    private static ProjectMemoryPipelineResult Fail(string correlationId, string message, List<ProjectMemoryPipelineStep> steps)
    {
        steps.Add(new ProjectMemoryPipelineStep { Name = "error", Ok = false, Detail = message });
        return Finish(correlationId, false, message, steps);
    }
}
