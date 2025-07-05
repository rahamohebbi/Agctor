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
                Console.WriteLine($"[CSharpFormatter] FormatAsync called with code length: {code.Length}");
                Console.WriteLine($"[CSharpFormatter] Input code sample (first 500 chars): '{code.Substring(0, Math.Min(500, code.Length))}'");
                
                var tree = CSharpSyntaxTree.ParseText(code, cancellationToken: cancellationToken);
                var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

                Console.WriteLine($"[CSharpFormatter] Parsed syntax tree successfully");

                using var workspace = new AdhocWorkspace();
                var options = workspace.Options
                    .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
                    .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, 4)
                    .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, 4);

                Console.WriteLine($"[CSharpFormatter] Configured formatting options - UseTabs: false, IndentationSize: 4, TabSize: 4");

                var formattedRoot = Formatter.Format(root, workspace, options, cancellationToken);
                var formattedCode = formattedRoot.ToFullString();
                
                Console.WriteLine($"[CSharpFormatter] Formatted code length: {formattedCode.Length}");
                Console.WriteLine($"[CSharpFormatter] Formatted code sample (first 500 chars): '{formattedCode.Substring(0, Math.Min(500, formattedCode.Length))}'");
                
                return (true, formattedCode, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSharpFormatter] Exception during formatting: {ex.Message}");
                Console.WriteLine($"[CSharpFormatter] Stack trace: {ex.StackTrace}");
                return (false, null, ex.Message);
            }
        }
    }
} 