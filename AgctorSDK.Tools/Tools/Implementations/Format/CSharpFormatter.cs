using System;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Tools.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;

namespace AgctorSDK.Core.Tools.Implementations.Format
{
    internal sealed class CSharpFormatter : ICodeFormatter
    {
        public string Language => "csharp";

        public bool IsAvailable => true; // Roslyn is bundled via NuGet

        public async Task<(bool IsSuccess, string? FormattedCode, string? Error)> FormatAsync(string code, CancellationToken cancellationToken = default)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(code, cancellationToken: cancellationToken);
                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

                using var workspace = new AdhocWorkspace();
                var options = workspace.Options
                    .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
                    .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, 4);

                var formattedRoot = Formatter.Format(root, workspace, options, cancellationToken);
                return (true, formattedRoot.ToFullString(), null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }
    }
} 