using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Builds read-only visual context appendices for coach/query personas (PRD-023).</summary>
public sealed class PersonVisualContextBuilder
{
    private readonly VisualAssetCatalogStore _catalog;
    private readonly IBlobStore _blobs;
    private readonly VisualStorageOptions _options;

    public PersonVisualContextBuilder(
        VisualAssetCatalogStore catalog,
        IBlobStore blobs,
        IOptions<VisualStorageOptions> options)
    {
        _catalog = catalog;
        _blobs = blobs;
        _options = options?.Value ?? new VisualStorageOptions();
    }

    public async Task<PersonVisualContextResult> BuildAsync(
        string projectRoot,
        string scenarioId,
        string userMessage,
        string visualIntent,
        IReadOnlyList<string>? entityKeys,
        int maxAssets,
        CancellationToken cancellationToken = default)
    {
        var scenarioSeg = PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
        var all = await _catalog.ListAsync(projectRoot, scenarioSeg, cancellationToken).ConfigureAwait(false);
        var filtered = FilterAssets(all, visualIntent, entityKeys, userMessage);
        var take = Math.Clamp(maxAssets <= 0 ? 3 : maxAssets, 1, 12);
        var selected = filtered.Take(take).ToList();

        var assets = new List<PersonVisualContextAsset>();
        foreach (var record in selected)
        {
            assets.Add(await ToContextAssetAsync(record, cancellationToken).ConfigureAwait(false));
        }

        var appendix = BuildAppendixText(visualIntent, userMessage, assets);
        return new PersonVisualContextResult(appendix, assets);
    }

    public async Task<IReadOnlyList<PersonVisualContextAsset>> ListForEntityAsync(
        string projectRoot,
        string scenarioId,
        string entityKey,
        int maxAssets,
        CancellationToken cancellationToken = default)
    {
        var scenarioSeg = PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
        var key = PersonaScenarioScope.SanitizeFolderSegment(entityKey).ToLowerInvariant();
        var all = await _catalog.ListAsync(projectRoot, scenarioSeg, cancellationToken).ConfigureAwait(false);
        var take = Math.Clamp(maxAssets <= 0 ? 10 : maxAssets, 1, 50);
        var list = all
            .Where(a => AssetReferencesEntity(a, key))
            .Take(take)
            .ToList();

        var result = new List<PersonVisualContextAsset>();
        foreach (var record in list)
            result.Add(await ToContextAssetAsync(record, cancellationToken).ConfigureAwait(false));
        return result;
    }

    private static IEnumerable<VisualAssetRecord> FilterAssets(
        IReadOnlyList<VisualAssetRecord> all,
        string visualIntent,
        IReadOnlyList<string>? entityKeys,
        string userMessage)
    {
        var intent = (visualIntent ?? "general").Trim().ToLowerInvariant();
        var keys = entityKeys?
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => PersonaScenarioScope.SanitizeFolderSegment(k).ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return all
            .Where(a => !string.Equals(a.State, VisualAssetStates.Deleted, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(a.State, VisualAssetStates.PendingUpload, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(a.State, VisualAssetStates.Failed, StringComparison.OrdinalIgnoreCase))
            .Where(a => keys == null || keys.Count == 0 || a.Subjects.Any(s => keys.Contains(s.EntityKey.ToLowerInvariant())))
            .Where(a => IntentAllows(a, intent))
            .OrderByDescending(a => a.UploadedAt);
    }

    private static bool IntentAllows(VisualAssetRecord record, string intent)
    {
        if (string.Equals(intent, "general", StringComparison.OrdinalIgnoreCase))
            return true;
        return record.Privacy.AllowAgentUse.Any(u =>
            string.Equals(u, intent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(u, "general", StringComparison.OrdinalIgnoreCase));
    }

    private static bool AssetReferencesEntity(VisualAssetRecord record, string entityKey) =>
        record.Subjects.Any(s =>
            string.Equals(
                PersonaScenarioScope.SanitizeFolderSegment(s.EntityKey).ToLowerInvariant(),
                entityKey,
                StringComparison.OrdinalIgnoreCase));

    private async Task<PersonVisualContextAsset> ToContextAssetAsync(
        VisualAssetRecord record,
        CancellationToken cancellationToken)
    {
        var item = new PersonVisualContextAsset
        {
            AssetId = record.AssetId,
            State = record.State,
            ContentType = record.Storage.ContentType,
            Caption = record.Context.UserCaption,
            SceneSummary = record.Extraction.SceneSummary,
            Subjects = record.Subjects.Select(s => s.EntityKey).ToList(),
            ExtractionStatus = record.Extraction.Status
        };

        if (string.Equals(record.State, VisualAssetStates.PendingUpload, StringComparison.OrdinalIgnoreCase))
            return item;

        try
        {
            var expiry = TimeSpan.FromSeconds(Math.Max(60, _options.PresignedViewExpirySeconds));
            var access = await _blobs
                .CreatePresignedGetAsync(record.Storage.Bucket, record.Storage.Key, expiry, cancellationToken)
                .ConfigureAwait(false);
            item.ViewUrl = access.Url;
            item.ViewUrlExpiresAt = access.ExpiresAt;
        }
        catch
        {
            // appendix still useful without URL
        }

        return item;
    }

    private static string BuildAppendixText(
        string visualIntent,
        string userMessage,
        IReadOnlyList<PersonVisualContextAsset> assets)
    {
        if (assets.Count == 0)
            return "Visual context: no matching photos in catalog for this scenario.";

        var sb = new StringBuilder();
        sb.AppendLine($"Visual context ({visualIntent}, {assets.Count} photo(s)):");
        foreach (var a in assets)
        {
            var who = a.Subjects.Count > 0 ? string.Join(", ", a.Subjects) : "(subjects not set)";
            sb.AppendLine($"- asset {a.AssetId}: state={a.State}; subjects={who}; extraction={a.ExtractionStatus}");
            if (!string.IsNullOrWhiteSpace(a.Caption))
                sb.AppendLine($"  caption: {a.Caption}");
            if (!string.IsNullOrWhiteSpace(a.SceneSummary))
                sb.AppendLine($"  scene: {a.SceneSummary}");
            if (!string.IsNullOrWhiteSpace(a.ViewUrl))
                sb.AppendLine($"  viewUrl: {a.ViewUrl}");
        }

        if (!string.IsNullOrWhiteSpace(userMessage))
            sb.AppendLine($"User message hint: {userMessage.Trim()}");
        return sb.ToString().TrimEnd();
    }
}

public sealed class PersonVisualContextResult
{
    public PersonVisualContextResult(string appendix, IReadOnlyList<PersonVisualContextAsset> assets)
    {
        Appendix = appendix;
        Assets = assets;
    }

    public string Appendix { get; }

    public IReadOnlyList<PersonVisualContextAsset> Assets { get; }
}

public sealed class PersonVisualContextAsset
{
    public string AssetId { get; set; } = "";

    public string State { get; set; } = "";

    public string? ContentType { get; set; }

    public string? Caption { get; set; }

    /// <summary>Stored scene description from vision extract or query fallback.</summary>
    public string? SceneSummary { get; set; }

    public List<string> Subjects { get; set; } = new();

    public string? ExtractionStatus { get; set; }

    public string? ViewUrl { get; set; }

    public DateTimeOffset? ViewUrlExpiresAt { get; set; }
}
