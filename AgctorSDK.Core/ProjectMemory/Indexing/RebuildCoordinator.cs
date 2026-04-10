using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Validation;

namespace AgctorSDK.Core.ProjectMemory.Indexing;

/// <summary>
/// Full rebuild: load → discover → validate → index (PRD §13.4).
/// </summary>
public sealed class RebuildCoordinator
{
    private readonly IProjectLoader _loader;
    private readonly IEntityRegistry _entities;
    private readonly IDocumentParser _parser;
    private readonly IRuntimeIndexStoreFactory _storeFactory;

    public RebuildCoordinator(
        IProjectLoader loader,
        IEntityRegistry entities,
        IDocumentParser parser,
        IRuntimeIndexStoreFactory storeFactory)
    {
        _loader = loader;
        _entities = entities;
        _parser = parser;
        _storeFactory = storeFactory;
    }

    public async Task<RebuildReport> RebuildAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var ctx = await _loader.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        var list = await _entities.DiscoverAsync(ctx, cancellationToken).ConfigureAwait(false);
        issues.AddRange(ProjectRebuildValidator.Validate(ctx, list));

        if (issues.Any(i => i.IsError))
            return new RebuildReport { Success = false, Issues = issues };

        await using var store = _storeFactory.Create(ctx);
        var indexBuilder = new RuntimeIndexBuilder(_parser, store);
        await indexBuilder.RebuildAsync(ctx, list, cancellationToken).ConfigureAwait(false);

        var logDir = Path.Combine(ctx.AgctorRoot, "logs", "rebuilds");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"rebuild-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
        await File.WriteAllTextAsync(logPath, FormatLog(ctx, list, issues), cancellationToken).ConfigureAwait(false);

        issues.Add(new ValidationIssue { Code = "ok", Message = "Rebuild completed.", IsError = false });
        return new RebuildReport { Success = true, Issues = issues, LogPath = logPath };
    }

    private static string FormatLog(LoadedProjectContext ctx, IReadOnlyList<EntityRecord> entities, List<ValidationIssue> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"projectId={ctx.Project.ProjectId} type={ctx.Project.ProjectType}");
        sb.AppendLine($"entities={entities.Count}");
        foreach (var i in issues)
            sb.AppendLine($"{(i.IsError ? "ERR" : "INF")} {i.Code}: {i.Message}");
        return sb.ToString();
    }
}
