using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>File-backed generic inbox with per-file coarse locking for read-modify-write.</summary>
public sealed class GenericInboxStore : IGenericInboxStore
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> FileLocks = new();

    private static object LockFor(string path) => FileLocks.GetOrAdd(path, _ => new object());

    public Task<int> DropPendingAsync(
        string projectRoot,
        IReadOnlyList<string> proposalIds,
        CancellationToken cancellationToken = default)
    {
        if (proposalIds.Count == 0)
            return Task.FromResult(0);

        var root = Path.GetFullPath(projectRoot.Trim());
        var path = GenericInboxPaths.PendingFile(root);
        if (!File.Exists(path))
            return Task.FromResult(0);

        var dropSet = proposalIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dropped = 0;
        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(path);
            GenericInboxPendingFile file;
            try
            {
                file = string.IsNullOrWhiteSpace(text)
                    ? new GenericInboxPendingFile()
                    : ProjectYamlSerializer.Deserialize<GenericInboxPendingFile>(text);
            }
            catch
            {
                return Task.FromResult(0);
            }

            file.Items ??= new List<GenericInboxPendingRow>();
            var before = file.Items.Count;
            file.Items = file.Items.Where(r => !dropSet.Contains(r.ProposalId)).ToList();
            dropped = before - file.Items.Count;
            File.WriteAllText(path, ProjectYamlSerializer.Serialize(file));
        }

        return Task.FromResult(dropped);
    }

    public Task<IReadOnlyList<GenericInboxPendingRow>> LoadPendingAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(projectRoot.Trim());
        var path = GenericInboxPaths.PendingFile(root);
        if (!File.Exists(path))
            return Task.FromResult<IReadOnlyList<GenericInboxPendingRow>>(Array.Empty<GenericInboxPendingRow>());

        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromResult<IReadOnlyList<GenericInboxPendingRow>>(Array.Empty<GenericInboxPendingRow>());

            GenericInboxPendingFile file;
            try
            {
                file = ProjectYamlSerializer.Deserialize<GenericInboxPendingFile>(text);
            }
            catch
            {
                return Task.FromResult<IReadOnlyList<GenericInboxPendingRow>>(Array.Empty<GenericInboxPendingRow>());
            }

            return Task.FromResult<IReadOnlyList<GenericInboxPendingRow>>(file.Items ?? new List<GenericInboxPendingRow>());
        }
    }

    public Task AppendPendingAsync(
        string projectRoot,
        string? scenarioSegment,
        IReadOnlyList<OutOfSchemaFactProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        if (proposals.Count == 0)
            return Task.CompletedTask;

        var root = Path.GetFullPath(projectRoot.Trim());
        var dir = GenericInboxPaths.InboxDirectory(root);
        if (!ProjectMemoryAccessGuard.IsAgctorRuntimePath(root, dir))
            throw new InvalidOperationException("Generic inbox path must stay under project .agctor/runtime.");

        Directory.CreateDirectory(dir);
        var path = GenericInboxPaths.PendingFile(root);
        var seg = string.IsNullOrWhiteSpace(scenarioSegment) ? "" : PersonaScenarioScope.SanitizeFolderSegment(scenarioSegment);

        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.Exists(path) ? File.ReadAllText(path) : "";
            GenericInboxPendingFile file;
            try
            {
                file = string.IsNullOrWhiteSpace(text)
                    ? new GenericInboxPendingFile()
                    : ProjectYamlSerializer.Deserialize<GenericInboxPendingFile>(text);
            }
            catch
            {
                file = new GenericInboxPendingFile();
            }

            file.Items ??= new List<GenericInboxPendingRow>();
            var confirmed = LoadConfirmedIds(root);

            var now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            foreach (var p in proposals)
            {
                if (confirmed.Contains(p.ProposalId))
                    continue;

                var existing = file.Items.FirstOrDefault(i =>
                    string.Equals(i.ProposalId, p.ProposalId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    // A repeated prompt should reopen the short confirmation window instead of leaving
                    // an old duplicate expired forever.
                    existing.EntityKey = p.EntityKey;
                    existing.KnowledgeType = p.KnowledgeType;
                    existing.Attribute = p.Attribute;
                    existing.Value = p.Value;
                    existing.Confidence = p.Confidence;
                    existing.Disposition = p.Disposition == OutOfSchemaDisposition.ImmediateConfirmation ? "immediate" : "review";
                    existing.ScenarioSegment = seg;
                    existing.QueuedAtUtc = now;
                    existing.UserPromptLine = p.UserPromptLine;
                    continue;
                }

                file.Items.Add(new GenericInboxPendingRow
                {
                    ProposalId = p.ProposalId,
                    EntityKey = p.EntityKey,
                    KnowledgeType = p.KnowledgeType,
                    Attribute = p.Attribute,
                    Value = p.Value,
                    Confidence = p.Confidence,
                    Disposition = p.Disposition == OutOfSchemaDisposition.ImmediateConfirmation ? "immediate" : "review",
                    ScenarioSegment = seg,
                    QueuedAtUtc = now,
                    UserPromptLine = p.UserPromptLine
                });
            }

            File.WriteAllText(path, ProjectYamlSerializer.Serialize(file));
        }

        return Task.CompletedTask;
    }

    public Task<GenericInboxPersistResult> PersistApprovedAsync(
        string projectRoot,
        string? scenarioSegment,
        IReadOnlyList<ApprovedGenericFact> approvals,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (approvals.Count == 0)
            return Task.FromResult(new GenericInboxPersistResult { Appended = 0, RejectedMismatch = 0, Errors = errors });

        var root = Path.GetFullPath(projectRoot.Trim());
        var dir = GenericInboxPaths.InboxDirectory(root);
        if (!ProjectMemoryAccessGuard.IsAgctorRuntimePath(root, dir))
        {
            errors.Add("Generic inbox path must stay under project .agctor/runtime.");
            return Task.FromResult(new GenericInboxPersistResult { Errors = errors });
        }

        Directory.CreateDirectory(dir);
        var seg = string.IsNullOrWhiteSpace(scenarioSegment) ? "" : PersonaScenarioScope.SanitizeFolderSegment(scenarioSegment);
        var pendingPath = GenericInboxPaths.PendingFile(root);
        var confirmedPath = GenericInboxPaths.ConfirmedFile(root);

        var appended = 0;
        var rejected = 0;
        var appendedProposalIds = new List<string>();

        lock (LockFor(confirmedPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cText = File.Exists(confirmedPath) ? File.ReadAllText(confirmedPath) : "";
            GenericInboxConfirmedFile confirmedFile;
            try
            {
                confirmedFile = string.IsNullOrWhiteSpace(cText)
                    ? new GenericInboxConfirmedFile()
                    : ProjectYamlSerializer.Deserialize<GenericInboxConfirmedFile>(cText);
            }
            catch
            {
                confirmedFile = new GenericInboxConfirmedFile();
            }

            confirmedFile.Items ??= new List<GenericInboxConfirmedRow>();
            var confirmedIds = confirmedFile.Items.Select(i => i.ProposalId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var a in approvals)
            {
                var intent = new MemoryIntent
                {
                    EntityKey = a.EntityKey,
                    KnowledgeType = a.KnowledgeType,
                    Attribute = a.Attribute,
                    Value = a.Value,
                    Confidence = a.Confidence
                };
                var expected = OutOfSchemaProposalFactory.ComputeProposalId(intent);
                if (!string.Equals(expected, a.ProposalId, StringComparison.OrdinalIgnoreCase))
                {
                    rejected++;
                    continue;
                }

                if (confirmedIds.Contains(a.ProposalId))
                    continue;

                confirmedFile.Items.Add(new GenericInboxConfirmedRow
                {
                    ProposalId = a.ProposalId,
                    EntityKey = a.EntityKey.Trim(),
                    KnowledgeType = a.KnowledgeType.Trim(),
                    Attribute = a.Attribute?.Trim(),
                    Value = a.Value.Trim(),
                    Confidence = a.Confidence,
                    ScenarioSegment = seg,
                    Source = "user_approved",
                    CapturedAtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                });
                confirmedIds.Add(a.ProposalId);
                appended++;
                appendedProposalIds.Add(a.ProposalId);
            }

            File.WriteAllText(confirmedPath, ProjectYamlSerializer.Serialize(confirmedFile));
        }

        lock (LockFor(pendingPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(pendingPath))
                return Task.FromResult(new GenericInboxPersistResult
                {
                    Appended = appended,
                    RejectedMismatch = rejected,
                    Errors = errors,
                    AppendedProposalIds = appendedProposalIds
                });

            var pText = File.ReadAllText(pendingPath);
            GenericInboxPendingFile pendingFile;
            try
            {
                pendingFile = string.IsNullOrWhiteSpace(pText)
                    ? new GenericInboxPendingFile()
                    : ProjectYamlSerializer.Deserialize<GenericInboxPendingFile>(pText);
            }
            catch
            {
                pendingFile = new GenericInboxPendingFile();
            }

            pendingFile.Items ??= new List<GenericInboxPendingRow>();
            var approvedIds = approvals.Select(a => a.ProposalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            pendingFile.Items = pendingFile.Items.Where(r => !approvedIds.Contains(r.ProposalId)).ToList();
            File.WriteAllText(pendingPath, ProjectYamlSerializer.Serialize(pendingFile));
        }

        return Task.FromResult(new GenericInboxPersistResult
        {
            Appended = appended,
            RejectedMismatch = rejected,
            Errors = errors,
            AppendedProposalIds = appendedProposalIds
        });
    }

    public Task<IReadOnlyList<GenericInboxConfirmedRow>> LoadConfirmedAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(projectRoot.Trim());
        var path = GenericInboxPaths.ConfirmedFile(root);
        if (!File.Exists(path))
            return Task.FromResult<IReadOnlyList<GenericInboxConfirmedRow>>(Array.Empty<GenericInboxConfirmedRow>());

        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromResult<IReadOnlyList<GenericInboxConfirmedRow>>(Array.Empty<GenericInboxConfirmedRow>());

            GenericInboxConfirmedFile file;
            try
            {
                file = ProjectYamlSerializer.Deserialize<GenericInboxConfirmedFile>(text);
            }
            catch
            {
                return Task.FromResult<IReadOnlyList<GenericInboxConfirmedRow>>(Array.Empty<GenericInboxConfirmedRow>());
            }

            return Task.FromResult<IReadOnlyList<GenericInboxConfirmedRow>>(file.Items ?? new List<GenericInboxConfirmedRow>());
        }
    }

    public Task<int> MarkReplayedAsync(
        string projectRoot,
        IReadOnlyList<string> proposalIds,
        string replayedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (proposalIds.Count == 0)
            return Task.FromResult(0);

        var root = Path.GetFullPath(projectRoot.Trim());
        var path = GenericInboxPaths.ConfirmedFile(root);
        if (!File.Exists(path))
            return Task.FromResult(0);

        var ids = proposalIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stamped = 0;
        lock (LockFor(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(path);
            GenericInboxConfirmedFile file;
            try
            {
                file = string.IsNullOrWhiteSpace(text)
                    ? new GenericInboxConfirmedFile()
                    : ProjectYamlSerializer.Deserialize<GenericInboxConfirmedFile>(text);
            }
            catch
            {
                return Task.FromResult(0);
            }

            file.Items ??= new List<GenericInboxConfirmedRow>();
            foreach (var item in file.Items)
            {
                if (!ids.Contains(item.ProposalId)) continue;
                item.ReplayedAtUtc = replayedAtUtc;
                stamped++;
            }

            File.WriteAllText(path, ProjectYamlSerializer.Serialize(file));
        }

        return Task.FromResult(stamped);
    }

    private static HashSet<string> LoadConfirmedIds(string projectRoot)
    {
        var path = GenericInboxPaths.ConfirmedFile(projectRoot);
        if (!File.Exists(path))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var f = ProjectYamlSerializer.DeserializeFromFile<GenericInboxConfirmedFile>(path);
            return f.Items?.Select(i => i.ProposalId).ToHashSet(StringComparer.OrdinalIgnoreCase)
                   ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
