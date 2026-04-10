using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Parsing;

namespace AgctorSDK.Core.ProjectMemory.Indexing;

/// <summary>
/// Parses canonical files and pushes rows into <see cref="IRuntimeIndexStore"/>.
/// </summary>
public sealed class RuntimeIndexBuilder
{
    private readonly IDocumentParser _parser;
    private readonly IRuntimeIndexStore _store;

    public RuntimeIndexBuilder(IDocumentParser parser, IRuntimeIndexStore store)
    {
        _parser = parser;
        _store = store;
    }

    public async Task RebuildAsync(
        LoadedProjectContext ctx,
        IReadOnlyList<EntityRecord> entities,
        CancellationToken cancellationToken = default)
    {
        await _store.EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await _store.RebuildProjectAsync(ctx, entities, _parser, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Hash helper for document rows (optional diagnostics).</summary>
    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    public static ParsedMarkdownDocument ParseOrEmpty(IDocumentParser parser, string text)
    {
        return parser.Parse(string.IsNullOrEmpty(text) ? "\n" : text);
    }
}
