using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.Rag;
using AgctorSDK.Host.Services.Scenarios;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services.Rag;

public sealed record RagMigrationReadiness(string ProviderId, bool Ready, string Status, string Message);

public sealed class RagMigrationFile
{
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public string Hash { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Message { get; set; } = "Ready to copy";
}

public sealed class RagMigrationJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ScenarioId { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string CollectionId { get; set; } = "";
    public string Source { get; set; } = "scenario";
    public string Root { get; set; } = "";
    public string DestinationKey { get; set; } = "";
    public bool Force { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "preview";
    public string Message { get; set; } = "Review the files before sending them.";
    public bool StopRequested { get; set; }
    public List<RagMigrationFile> Files { get; set; } = [];
}

/// <summary>Durable, non-destructive markdown export. Accepted means acknowledged by the provider,
/// not necessarily searchable. Interrupted sends are never automatically replayed.</summary>
public sealed class RagMigrationService : BackgroundService
{
    private readonly IRagProviderAdapterFactory _factory;
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _memory;
    private readonly IOptionsMonitor<RagOptions> _options;
    private readonly IScenarioCatalog _scenarios;
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, RagMigrationJob> _jobs = new();
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public RagMigrationService(IRagProviderAdapterFactory factory, IOptionsMonitor<ProjectMemoryAgentOptions> memory,
        IOptionsMonitor<RagOptions> options, IScenarioCatalog scenarios, string directory)
    {
        (_factory, _memory, _options, _scenarios, _directory) = (factory, memory, options, scenarios, directory);
        Directory.CreateDirectory(directory);
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var job = JsonSerializer.Deserialize<RagMigrationJob>(File.ReadAllText(path), Json);
            if (job == null) continue;
            if (job.Status is "queued" or "running")
            {
                job.Status = "interrupted";
                job.Message = "Host restarted. Review uncertain files in the provider before retrying.";
                foreach (var file in job.Files.Where(f => f.Status == "sending"))
                { file.Status = "uncertain"; file.Message = "Interrupted during submission; the provider may have accepted this file."; }
                Save(job);
            }
            _jobs[job.Id] = job;
        }
    }
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    private void Save(RagMigrationJob job)
    {
        var path = System.IO.Path.Combine(_directory, job.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(job, Json));
        File.Move(path + ".tmp", path, true);
    }
    public RagMigrationJob? Get(string id) { lock (_gate) return _jobs.TryGetValue(id, out var job) ? Copy(job) : null; }
    public IReadOnlyList<RagMigrationJob> List(string scenarioId)
    { lock (_gate) return _jobs.Values.Where(j => j.ScenarioId == scenarioId && j.Status != "preview").OrderByDescending(j => j.CreatedAt).Take(20).Select(Copy).ToArray(); }
    private string SafeDiagnostic(string message)
    {
        var options = _options.CurrentValue;
        foreach (var secret in new[] { options.LightRAG.ApiKey, options.Graphiti.ApiKey, options.Cognee.LlmApiKey })
            if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, "[redacted]", StringComparison.Ordinal);
        message = Regex.Replace(message, @"https?://[^\s)]+", match =>
            Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority).Replace(uri.UserInfo + "@", "", StringComparison.Ordinal) + uri.AbsolutePath : "[endpoint]");
        return message.Length > 600 ? message[..600] + "…" : message;
    }
    public async Task<RagMigrationReadiness> CheckProviderAsync(string providerId, CancellationToken ct)
    {
        if (providerId == RagProviderIds.None || !_factory.IsProviderAvailable(providerId)) throw new ArgumentException("Choose an external RAG provider.");
        try
        {
            var health = await _factory.CreateProvider(providerId).GetHealthAsync(ct);
            return new(providerId, health.Status is RagHealthStatus.Healthy or RagHealthStatus.Degraded,
                health.Status.ToString(), SafeDiagnostic(health.Message));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { return new(providerId, false, "Unavailable", providerId + " could not be reached. Start the provider and check its configured address."); }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string Destination(string provider, string collection) => Hash(provider + "\n" + collection + "\n" + (provider switch
    {
        RagProviderIds.LightRag => JsonSerializer.Serialize(_options.CurrentValue.LightRAG),
        RagProviderIds.Graphiti => JsonSerializer.Serialize(_options.CurrentValue.Graphiti),
        RagProviderIds.Cognee => JsonSerializer.Serialize(_options.CurrentValue.Cognee),
        _ => throw new ArgumentException("Choose an external RAG provider.")
    }));
    private bool Accepted(RagMigrationJob job, RagMigrationFile file) => _jobs.Values.Any(j => j.Id != job.Id &&
        j.Root == job.Root && j.DestinationKey == job.DestinationKey && j.Files.Any(f => f.Path == file.Path && f.Hash == file.Hash && f.Status == "accepted"));
    private static string SafePath(string root, string relative)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!PersonaScenarioScope.IsUnderProjectRoot(root, path) || path == root) throw new InvalidOperationException("Path is outside the memory folder.");
        for (var p = path; p != root; p = System.IO.Path.GetDirectoryName(p)!)
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Symbolic links are excluded.");
        return path;
    }
    public async Task<RagMigrationJob> PreviewAsync(string scenarioId, string providerId, string source, bool force, CancellationToken ct)
    {
        if (_scenarios.Get(scenarioId) == null) throw new ArgumentException("Scenario not found.");
        if (!_factory.IsProviderAvailable(providerId) || providerId == RagProviderIds.None) throw new ArgumentException("Choose an installed external RAG provider.");
        if (source is not ("scenario" or "legacy")) throw new ArgumentException("Unknown memory source.");
        var root = _memory.CurrentValue.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Configure the Project Memory folder first.");
        root = System.IO.Path.GetFullPath(root);
        var segment = PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
        if (segment is "." or "..") throw new ArgumentException("Invalid scenario folder.");
        var relative = source == "legacy" ? "people" : "scenarios/" + segment;
        var job = new RagMigrationJob { ScenarioId = scenarioId, ProviderId = providerId, CollectionId = scenarioId,
            Root = root, Source = source, Force = force, DestinationKey = Destination(providerId, scenarioId) };
        var folder = System.IO.Path.Combine(root, relative);
        if (Directory.Exists(folder))
        {
            SafePath(root, relative);
            Scan(folder);
        }
        long bytes = 0;
        foreach (var file in job.Files.Where(f => f.Status == "pending"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = SafePath(root, file.Path);
                file.Bytes = new FileInfo(path).Length;
                bytes += file.Bytes;
                if (file.Bytes > 512_000) throw new InvalidOperationException("Larger than the 500 KB per-file limit.");
                if (bytes > 32_000_000) throw new ArgumentException("This source exceeds the 32 MB migration limit. Use the provider's bulk import tools.");
                var content = await File.ReadAllTextAsync(path, ct);
                if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Empty markdown file.");
                file.Hash = Hash(content);
                lock (_gate)
                {
                    if (!force && Accepted(job, file)) { file.Status = "unchanged"; file.Message = "Previously accepted; unchanged."; }
                    else if (_jobs.Values.Any(j => j.Root == job.Root && j.DestinationKey == job.DestinationKey && j.Files.Any(f => f.Path == file.Path && f.Hash == file.Hash && f.Status == "uncertain")))
                    { file.Status = "review"; file.Message = "A previous submission was interrupted. Check the provider for this file before selecting it to retry."; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { file.Status = "excluded"; file.Message = ex is InvalidOperationException ? ex.Message : "File could not be read."; }
        }
        lock (_gate) { _jobs.Add(job.Id, job); Save(job); }
        return Copy(job);

        void Scan(string dir)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(dir).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (job.Files.Count >= 2000) throw new ArgumentException("This source exceeds the 2,000 file preview limit. Use the provider's bulk import tools.");
                var attributes = File.GetAttributes(path);
                var rel = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { job.Files.Add(new() { Path = rel, Status = "excluded", Message = "Symbolic links are excluded." }); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                { if (!System.IO.Path.GetFileName(path).StartsWith('.') && System.IO.Path.GetFileName(path) != "logs") Scan(path); }
                else if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) job.Files.Add(new() { Path = rel });
            }
        }
    }
    public RagMigrationJob Start(string id, IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job)) throw new ArgumentException("Preview not found.");
            if (job.Status != "preview") return Copy(job); // Double clicks are idempotent.
            if (job.CreatedAt < DateTimeOffset.UtcNow.AddHours(-1)) throw new ArgumentException("Preview expired. Preview the files again.");
            if (_jobs.Values.Any(j => j.Status is "queued" or "running")) throw new ArgumentException("A migration is already running. Wait for it to finish.");
            if (Destination(job.ProviderId, job.CollectionId) != job.DestinationKey) throw new ArgumentException("Provider settings changed. Preview again.");
            var selected = paths.ToHashSet(StringComparer.Ordinal);
            if (selected.Count == 0 || selected.Any(p => !job.Files.Any(f => f.Path == p && (f.Status is "pending" or "review")))) throw new ArgumentException("Select files from this preview.");
            foreach (var file in job.Files.Where(f => f.Status is "pending" or "review"))
            {
                if (selected.Contains(file.Path)) file.Status = "pending";
                else { file.Status = "excluded"; file.Message = "Not selected."; }
            }
            job.Status = "queued"; job.Message = "Queued. You can close this dialog; progress is saved.";
            Save(job); _queue.Writer.TryWrite(job.Id); return Copy(job);
        }
    }
    public RagMigrationJob Stop(string id)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job)) throw new ArgumentException("Migration not found.");
            if (job.Status is "queued" or "running") { job.StopRequested = true; job.Message = "Stopping after the current provider request finishes."; Save(job); }
            return Copy(job);
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                RagMigrationJob job;
                lock (_gate) { job = _jobs[id]; job.Status = "running"; job.Message = "Checking provider health…"; Save(job); }
                try { await RunAsync(job, stoppingToken); }
                catch (Exception ex)
                {
                    lock (_gate)
                    {
                        job.Status = "interrupted";
                        job.Message = ex is OperationCanceledException ? "Host stopped. Review uncertain files before retrying." : "Migration stopped. Check provider connectivity and retry remaining files.";
                        foreach (var file in job.Files.Where(f => f.Status == "sending"))
                        { file.Status = "uncertain"; file.Message = "No acknowledgement received; check the provider before retrying to avoid duplicates."; }
                        Save(job);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown can cancel the channel while it is waiting for work.
        }
    }
    private async Task RunAsync(RagMigrationJob job, CancellationToken ct)
    {
        var adapter = _factory.CreateProvider(job.ProviderId);
        var health = await CheckProviderAsync(job.ProviderId, ct);
        if (!health.Ready)
        { lock (_gate) { job.Status = "failed"; job.Message = health.Message + " No files were sent. Open RAG providers to start or configure " + job.ProviderId + ", then check the connection and retry."; Save(job); } return; }
        // Cognee cognifies each call. Send one bounded batch, retaining per-file provenance in the text.
        var pending = job.Files.Where(f => f.Status == "pending").ToArray();
        var batches = job.ProviderId == RagProviderIds.Cognee ? new[] { pending } : pending.Select(f => new[] { f }).ToArray();
        foreach (var batch in batches)
        {
            lock (_gate) if (job.StopRequested) break;
            if (Destination(job.ProviderId, job.CollectionId) != job.DestinationKey || System.IO.Path.GetFullPath(_memory.CurrentValue.ProjectRoot!) != job.Root)
                throw new InvalidOperationException("Configuration changed.");
            var ready = new List<RagMigrationFile>();
            var contents = new List<string>();
            foreach (var file in batch)
            {
                try
                {
                    var path = SafePath(job.Root, file.Path);
                    if (new FileInfo(path).Length > 512_000) throw new InvalidOperationException();
                    var content = await File.ReadAllTextAsync(path, ct);
                    if (Hash(content) != file.Hash) throw new InvalidOperationException();
                    lock (_gate)
                    {
                        if (!job.Force && Accepted(job, file))
                        { file.Status = "unchanged"; file.Message = "Accepted since this preview; skipped."; Save(job); continue; }
                    }
                    ready.Add(file); contents.Add(content);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                { lock (_gate) { file.Status = "failed"; file.Message = "File changed or became unreadable since preview. Preview again."; Save(job); } }
            }
            if (ready.Count == 0) continue;
            lock (_gate)
            {
                if (job.StopRequested) break;
                foreach (var file in ready) { file.Status = "sending"; file.Message = "Waiting for provider acknowledgement…"; }
                job.Message = "Sending markdown to " + job.ProviderId + "…"; Save(job);
            }
            var contentToSend = ready.Count == 1 ? contents[0] : string.Join("\n\n", ready.Select((f, i) => $"--- {f.Path} ---\n{contents[i]}"));
            var result = await adapter.IngestAsync(new RagIngestRequest(ready.Count == 1 ? ready[0].Path : $"{job.ScenarioId}-migration-{job.Id}.md", job.CollectionId, contentToSend), ct);
            lock (_gate)
            {
                foreach (var file in ready)
                { file.Status = result.Success ? "accepted" : "failed"; file.Message = result.Success ? "Accepted by provider. Indexing may still be in progress." : SafeDiagnostic(result.Message) + " Check provider logs before retrying; partial processing is possible."; }
                Save(job);
            }
        }
        lock (_gate)
        {
            job.Status = job.StopRequested ? "stopped" : job.Files.Any(f => f.Status == "failed") ? "partial" : "completed";
            job.Message = job.StopRequested ? "Stopped. Accepted files are saved; remaining files were not sent." : "Submission finished. Markdown originals are intact. Provider indexing may continue; verify retrieval in Playground's RAG inspector.";
            Save(job);
        }
    }
}
