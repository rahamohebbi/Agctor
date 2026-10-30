using System.Collections.Concurrent;
using System.Diagnostics;
using AgctorSDK.Core.SceneMemory;

namespace AgctorSDK.Host.Services;

public sealed record SceneEvaluationFact(string Subject, string Property, string? Value = null, string? Target = null);
public sealed record SceneEvaluationEvent(string Mode, IReadOnlyList<string> Participants);
public sealed record SceneEvaluationCase(string Id, string Category, string Title, IReadOnlyList<string> Prompts,
    IReadOnlyList<ScenePromptEntity> ExpectedEntities, IReadOnlyList<SceneEvaluationFact> ExpectedFacts,
    IReadOnlyList<SceneEvaluationFact>? ForbiddenFacts = null, IReadOnlyList<SceneEvaluationEvent>? ExpectedEvents = null,
    bool ExpectQuestion = false);
public sealed record SceneEvaluationCheck(string Kind, string Expected, bool Passed, string? Actual = null);
public sealed record SceneEvaluationCaseResult(string CaseId, string Category, string Title, string Model, bool Passed,
    double Score, long DurationMs, long HostMemoryDeltaBytes, string? ErrorCategory, string? Error,
    IReadOnlyList<SceneEvaluationCheck> Checks, SceneWorkspace? ActualState);
public sealed record SceneEvaluationStartRequest(IReadOnlyList<string> Models, IReadOnlyList<string>? CaseIds = null,
    IReadOnlyList<string>? Categories = null, int? Limit = null);
public sealed record SceneEvaluationJobSnapshot(string Id, string Status, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt,
    int Total, int Completed, int Passed, IReadOnlyList<string> Models, IReadOnlyList<SceneEvaluationCaseResult> Results,
    string? Error = null);

/// <summary>
/// Runs model evaluations against isolated in-memory scenes. It never reads or writes user Scene Memory workspaces.
/// Jobs are intentionally process-local; exported JSON is the durable artifact for this first evaluation version.
/// </summary>
public sealed class SceneMemoryEvaluationService(IScenePromptInterpreter interpreter) : IDisposable
{
    private readonly ConcurrentDictionary<string, EvaluationJob> _jobs = new(StringComparer.Ordinal);
    public static IReadOnlyList<SceneEvaluationCase> Catalog { get; } = BuildCatalog();

    public SceneEvaluationJobSnapshot Start(SceneEvaluationStartRequest request)
    {
        var models = (request.Models ?? Array.Empty<string>()).Select(m => m?.Trim()).Where(m => !string.IsNullOrEmpty(m))
            .Distinct(StringComparer.Ordinal).Cast<string>().ToArray();
        if (models.Length is < 1 or > 3) throw new ArgumentException("Choose between one and three installed models.");
        if (models.Any(m => m.Length > 200)) throw new ArgumentException("A model name is too long.");

        var cases = Catalog.AsEnumerable();
        if (request.CaseIds is { Count: > 0 })
        {
            var wanted = request.CaseIds.ToHashSet(StringComparer.Ordinal);
            cases = cases.Where(c => wanted.Contains(c.Id));
        }
        if (request.Categories is { Count: > 0 })
        {
            var wanted = request.Categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            cases = cases.Where(c => wanted.Contains(c.Category));
        }
        if (request.Limit is < 1 or > 100) throw new ArgumentException("The case limit must be between 1 and 100.");
        if (request.Limit is int limit) cases = TakeBalanced(cases, limit);
        var selected = cases.ToArray();
        if (selected.Length == 0) throw new ArgumentException("No evaluation cases match this selection.");

        PruneFinishedJobs();
        var job = new EvaluationJob(Guid.NewGuid().ToString("N"), models, selected);
        if (!_jobs.TryAdd(job.Id, job)) throw new InvalidOperationException("Could not create the evaluation job.");
        _ = Task.Run(() => RunAsync(job));
        return job.Snapshot();
    }

    public SceneEvaluationJobSnapshot Get(string id) => _jobs.TryGetValue(id, out var job)
        ? job.Snapshot()
        : throw new KeyNotFoundException("Evaluation run not found. Runs are cleared when the Host restarts.");

    public SceneEvaluationJobSnapshot Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job)) throw new KeyNotFoundException("Evaluation run not found.");
        job.Cancel();
        return job.Snapshot();
    }

    internal static IEnumerable<SceneEvaluationCase> TakeBalanced(IEnumerable<SceneEvaluationCase> source, int limit)
    {
        var queues = source.GroupBy(c => c.Category).Select(g => new Queue<SceneEvaluationCase>(g)).ToArray();
        var selected = new List<SceneEvaluationCase>(limit);
        while (selected.Count < limit && queues.Any(q => q.Count > 0))
            foreach (var queue in queues)
                if (selected.Count < limit && queue.TryDequeue(out var item)) selected.Add(item);
        return selected;
    }

    private void PruneFinishedJobs()
    {
        if (_jobs.Count < 12) return;
        foreach (var id in _jobs.Values.Select(j => j.Snapshot()).Where(j => j.FinishedAt != null)
                     .OrderBy(j => j.FinishedAt).Take(Math.Max(1, _jobs.Count - 11)).Select(j => j.Id))
            _jobs.TryRemove(id, out _);
    }

    private async Task RunAsync(EvaluationJob job)
    {
        job.SetStatus("running");
        try
        {
            foreach (var model in job.Models)
            foreach (var testCase in job.Cases)
            {
                job.Token.ThrowIfCancellationRequested();
                job.Add(await RunCaseAsync(testCase, model, job.Token));
            }
            job.Finish("completed");
        }
        catch (OperationCanceledException) { job.Finish("cancelled"); }
        catch (Exception ex) { job.Finish("failed", ex.Message); }
    }

    private async Task<SceneEvaluationCaseResult> RunCaseAsync(SceneEvaluationCase testCase, string model, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = Environment.WorkingSet;
        var workspace = new SceneWorkspace("evaluation", testCase.Title, DateTimeOffset.UtcNow,
            Array.Empty<SceneEntity>(), Array.Empty<SceneFact>());
        try
        {
            var questionCount = 0;
            foreach (var prompt in testCase.Prompts)
            {
                var extraction = await interpreter.InterpretAsync(workspace, prompt, model, ct);
                questionCount += extraction.Questions.Count;
                var preview = new ScenePromptPreview(Guid.NewGuid().ToString("N"), ScenePromptReducer.Revision(workspace),
                    prompt, testCase.Title, model, extraction);
                workspace = ScenePromptReducer.Apply(workspace, preview, allowQuestions: true);
            }
            stopwatch.Stop();
            var checks = Score(testCase, workspace, questionCount);
            var score = checks.Count == 0 ? 1 : checks.Count(c => c.Passed) / (double)checks.Count;
            return new(testCase.Id, testCase.Category, testCase.Title, model, checks.All(c => c.Passed), score,
                stopwatch.ElapsedMilliseconds, Environment.WorkingSet - memoryBefore, FailureCategory(checks), null, checks, workspace);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var category = ex switch
            {
                HttpRequestException => "model_unavailable",
                ArgumentException => "invalid_interpretation",
                InvalidOperationException => "invalid_state_transition",
                _ => "runtime_error"
            };
            return new(testCase.Id, testCase.Category, testCase.Title, model, false, 0,
                stopwatch.ElapsedMilliseconds, Environment.WorkingSet - memoryBefore, category, ex.Message,
                Array.Empty<SceneEvaluationCheck>(), null);
        }
    }

    internal static IReadOnlyList<SceneEvaluationCheck> Score(SceneEvaluationCase testCase, SceneWorkspace workspace, int questionCount)
    {
        var checks = new List<SceneEvaluationCheck>();
        var byName = workspace.Entities.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var current = workspace.History.Where(f => !f.IsEvent).GroupBy(f => (f.EntityId, f.Property), new FactKeyComparer())
            .ToDictionary(g => g.Key, g => g.Last(), new FactKeyComparer());

        foreach (var expected in testCase.ExpectedEntities)
        {
            var found = byName.TryGetValue(expected.Name, out var entity);
            checks.Add(new("entity", $"{expected.Kind} {expected.Name}", found && entity!.Kind == expected.Kind,
                found ? $"{entity!.Kind} {entity.Name}" : "missing"));
        }
        foreach (var fact in testCase.ExpectedFacts)
            checks.Add(CheckFact(fact, byName, current, shouldExist: true));
        foreach (var fact in testCase.ForbiddenFacts ?? Array.Empty<SceneEvaluationFact>())
            checks.Add(CheckFact(fact, byName, current, shouldExist: false));
        foreach (var expected in testCase.ExpectedEvents ?? Array.Empty<SceneEvaluationEvent>())
        {
            var participantIds = expected.Participants.Where(byName.ContainsKey).Select(n => byName[n].Id).ToHashSet(StringComparer.Ordinal);
            var matching = workspace.History.Where(f => f.IsEvent && string.Equals(f.EventMode, expected.Mode, StringComparison.OrdinalIgnoreCase))
                .Any(f => participantIds.All(id => (f.ParticipantIds ?? Array.Empty<string>()).Contains(id)));
            checks.Add(new("event", $"{expected.Mode} event with {string.Join(", ", expected.Participants)}", matching,
                matching ? "recorded" : "missing"));
        }
        checks.Add(new("question", testCase.ExpectQuestion ? "at least one clarification question" : "no clarification questions",
            testCase.ExpectQuestion ? questionCount > 0 : questionCount == 0, questionCount.ToString()));
        return checks;
    }

    private static SceneEvaluationCheck CheckFact(SceneEvaluationFact expected,
        IReadOnlyDictionary<string, SceneEntity> byName,
        IReadOnlyDictionary<(string EntityId, string Property), SceneFact> current, bool shouldExist)
    {
        var label = $"{expected.Subject}.{expected.Property}" + (expected.Target is not null ? $" → {expected.Target}" : $" = {expected.Value}");
        var matches = false;
        var actual = "subject missing";
        if (byName.TryGetValue(expected.Subject, out var subject))
        {
            if (current.TryGetValue((subject.Id, expected.Property), out var fact) && !fact.Removed)
            {
                var target = fact.RelatedEntityId is null ? null : byName.Values.FirstOrDefault(e => e.Id == fact.RelatedEntityId)?.Name;
                matches = (expected.Value is null || Equal(expected.Value, fact.Value)) &&
                    (expected.Target is null || Equal(expected.Target, target));
                actual = target is null ? fact.Value : target;
            }
            else actual = "missing";
        }
        return new(shouldExist ? "fact" : "forbidden_fact", shouldExist ? label : "not " + label,
            shouldExist ? matches : !matches, actual);
    }

    private static bool Equal(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? FailureCategory(IReadOnlyList<SceneEvaluationCheck> checks) => checks.FirstOrDefault(c => !c.Passed)?.Kind switch
    {
        "entity" => "missing_or_wrong_entity", "fact" => "missing_or_wrong_fact", "forbidden_fact" => "invented_fact",
        "event" => "missing_event", "question" => "ambiguity_error", _ => null
    };

    private sealed class FactKeyComparer : IEqualityComparer<(string EntityId, string Property)>
    {
        public bool Equals((string EntityId, string Property) x, (string EntityId, string Property) y) =>
            x.EntityId == y.EntityId && string.Equals(x.Property, y.Property, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string EntityId, string Property) obj) => HashCode.Combine(obj.EntityId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Property));
    }

    private sealed class EvaluationJob(string id, string[] models, SceneEvaluationCase[] cases)
    {
        private readonly object _gate = new();
        private readonly List<SceneEvaluationCaseResult> _results = [];
        private readonly CancellationTokenSource _cancellation = new();
        private string _status = "queued";
        private string? _error;
        private DateTimeOffset? _finishedAt;
        public string Id { get; } = id;
        public string[] Models { get; } = models;
        public SceneEvaluationCase[] Cases { get; } = cases;
        public CancellationToken Token => _cancellation.Token;
        public void SetStatus(string status) { lock (_gate) _status = status; }
        public void Add(SceneEvaluationCaseResult result) { lock (_gate) _results.Add(result); }
        public void Cancel() => _cancellation.Cancel();
        public void Finish(string status, string? error = null) { lock (_gate) { _status = status; _error = error; _finishedAt = DateTimeOffset.UtcNow; } }
        public SceneEvaluationJobSnapshot Snapshot()
        {
            lock (_gate) return new(Id, _status, CreatedAt, _finishedAt, Models.Length * Cases.Length, _results.Count,
                _results.Count(r => r.Passed), Models, _results.ToArray(), _error);
        }
        private DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<SceneEvaluationCase> BuildCatalog()
    {
        var cases = new List<SceneEvaluationCase>(100);
        var people = new[] { "Mara", "Niko", "Jo", "Ari", "Lena", "Omar", "Iris", "Theo", "Zara", "Noah", "Mina", "Eli" };
        var objects = new[] { "Key", "Pen", "Phone", "Mug", "Book", "Wallet", "Camera", "Lantern", "Scarf", "Bottle", "Notebook", "Umbrella", "Ticket", "Watch", "Backpack", "Apple" };
        var places = new[] { "Kitchen", "Hall", "Garden", "Studio", "Station", "Library", "Workshop", "Balcony", "Office", "Cafe", "Bedroom", "Courtyard" };
        ScenePromptEntity E(string name, string kind) => new(name.ToLowerInvariant(), name, kind);
        SceneEvaluationFact F(string subject, string property, string? value = null, string? target = null) => new(subject, property, value, target);
        void Add(string category, int number, string title, string[] prompts, ScenePromptEntity[] entities, SceneEvaluationFact[] facts,
            SceneEvaluationFact[]? forbidden = null, SceneEvaluationEvent[]? events = null, bool question = false) =>
            cases.Add(new($"{category}-{number:00}", category, title, prompts, entities, facts, forbidden, events, question));

        for (var i = 0; i < 16; i++)
        {
            var person = people[i % people.Length]; var item = objects[i];
            Add("possession", i + 1, $"{person} carries {item}", [$"{person} carries the {item.ToLowerInvariant()}."],
                [E(person, "Character"), E(item, "Object")], [F(item, "held_by", target: person)]);
        }
        var colors = new[] { "red", "blue", "green", "black", "white", "yellow", "silver", "brown", "purple", "orange", "gray", "gold" };
        for (var i = 0; i < 12; i++)
        {
            var person = people[i]; var item = $"{colors[i]} {objects[i]}"; var expectedItem = char.ToUpperInvariant(item[0]) + item[1..];
            Add("ownership", i + 1, $"{person} owns a {item}", [$"{person} owns a {item}."],
                [E(person, "Character"), E(expectedItem, "Object")], [F(expectedItem, "owned_by", target: person), F(expectedItem, "color", colors[i])],
                [F(expectedItem, "held_by")]);
        }
        for (var i = 0; i < 12; i++)
        {
            var from = people[i]; var to = people[(i + 1) % people.Length]; var item = objects[i];
            Add("transfer", i + 1, $"{from} transfers {item} to {to}",
                [$"{from} carries the {item.ToLowerInvariant()}.", $"{from} gives the {item.ToLowerInvariant()} to {to}, and {to} takes it."],
                [E(from, "Character"), E(to, "Character"), E(item, "Object")], [F(item, "held_by", target: to)]);
        }
        for (var i = 0; i < 8; i++)
        {
            var person = people[i]; var item = objects[i];
            Add("lifecycle", i + 1, $"{person} discards {item}",
                [$"{person} carries the {item.ToLowerInvariant()}.", $"{person} throws away the {item.ToLowerInvariant()}."],
                [E(person, "Character"), E(item, "Object")], [F(item, "disposition", "discarded")], [F(item, "held_by", target: person)]);
        }
        var foods = new[] { "Bread", "Apple", "Sandwich", "Cookie", "Pear", "Banana", "Cake", "Carrot" };
        for (var i = 0; i < 8; i++)
        {
            var person = people[i]; var item = foods[i];
            Add("lifecycle", i + 9, $"{person} consumes {item}", [$"{person} eats the {item.ToLowerInvariant()}."],
                [E(person, "Character"), E(item, "Object")], [F(item, "status", "consumed"), F(item, "consumed_by", target: person)],
                [F(item, "held_by")]);
        }
        var abilities = new[] { "sing", "dance", "swim", "cook", "paint", "drive", "read", "climb", "write", "skate" };
        for (var i = 0; i < 10; i++)
        {
            var person = people[i]; var ability = abilities[i];
            Add("people", i + 1, $"{person} has an ability and becomes sick", [$"{person} can {ability} but gets sick."],
                [E(person, "Character")], [F(person, $"ability_{ability}", "true"), F(person, "health", "sick")]);
        }
        for (var i = 0; i < 10; i++)
        {
            var person = people[(i + 2) % people.Length];
            Add("people", i + 11, $"{person} dies", [$"{person} is walking, then {person} dies."],
                [E(person, "Character")], [F(person, "life_state", "dead")], [F(person, "activity", "walking")]);
        }
        for (var i = 0; i < 12; i++)
        {
            var person = people[i]; var place = places[i];
            Add("locations", i + 1, $"{person} enters {place}", [$"{person} enters the {place.ToLowerInvariant()}."],
                [E(person, "Character"), E(place, "Location")], [F(person, "located_at", target: place)]);
        }
        var conditions = new[] { "broken", "wet", "open", "locked", "empty", "hot", "cold", "dirty" };
        for (var i = 0; i < 8; i++)
        {
            var person = people[i]; var item = objects[i]; var condition = conditions[i];
            Add("objects", i + 1, $"{item} is {condition}", [$"{person} finds the {item.ToLowerInvariant()}, and it is {condition}."],
                [E(person, "Character"), E(item, "Object")], [F(item, condition is "open" or "locked" ? (condition == "open" ? "state" : "lock_state") : "condition", condition)]);
        }
        for (var i = 0; i < 4; i++)
        {
            var from = people[i]; var to = people[i + 4]; var item = objects[i];
            Add("intent", i + 1, $"{from} plans a transfer", [$"{from} plans to give the {item.ToLowerInvariant()} to {to} tomorrow."],
                [E(from, "Character"), E(to, "Character"), E(item, "Object")], [], [F(item, "held_by", target: to)],
                [new("intended", [from, to, item])]);
        }
        if (cases.Count != 100 || cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != 100)
            throw new InvalidOperationException("Scene evaluation catalog must contain 100 uniquely identified cases.");
        return cases;
    }

    public void Dispose()
    {
        foreach (var job in _jobs.Values) job.Cancel();
    }
}
