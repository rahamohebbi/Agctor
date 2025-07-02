using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Tools.Implementations.LanguageAdapters
{
    /// <summary>
    /// Provides language-specific services (AST selector resolution, formatting, etc.)
    /// to the otherwise language-agnostic CodeEditorTool.
    /// </summary>
    public interface ILanguageAdapter
    {
        /// <summary>Primary file extension handled by this adapter (e.g. ".cs").</summary>
        string Extension { get; }

        /// <summary>Attempts to insert <paramref name="snippet"/> after the element addressed by <paramref name="selector"/>; returns null when selector not resolved.</summary>
        string? InsertBySelector(string source, string selector, string snippet);

        /// <summary>Attempts to replace the element addressed by <paramref name="selector"/> with <paramref name="replacement"/>; returns null when selector not resolved.</summary>
        string? ReplaceBySelector(string source, string selector, string replacement);

        /// <summary>Optional formatter; returns formatted source or null when formatting failed or unsupported.</summary>
        Task<(bool ok, string? formatted)> TryFormatAsync(string source, CancellationToken ct = default);
    }
} 