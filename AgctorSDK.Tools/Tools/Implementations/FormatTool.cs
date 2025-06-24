using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.Tools.Abstractions;
using AgctorSDK.Core.Tools.Models;
using AgctorSDK.Core.Tools.Implementations.Format;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Tools.Implementations
{
    /// <summary>
    /// Generic tool actor that formats source code in multiple languages using concrete <see cref="ICodeFormatter"/> implementations.
    /// </summary>
    public sealed class FormatTool : Agent, IToolActor
    {
        public FormatTool(string id) : base(id) { }

        public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Payload is ProcessPromptMessage ppm)
            {
                await ProcessPromptAsync(ppm.Prompt, cancellationToken);
                return new MessageEnvelope(new ToolResult { IsSuccess = true });
            }
            else if (envelope.Payload is ToolRequest tr)
            {
                var result = await HandleAsync(tr, cancellationToken);
                return new MessageEnvelope(result);
            }

            return await base.ReceiveAsync(envelope, cancellationToken);
        }

        public override async Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            var request = ParsePrompt(prompt);
            var result = await HandleAsync(request, cancellationToken);

            if (result.IsSuccess)
                await FinalizeTask(result, cancellationToken);
            else
                await FinalizeTaskAsFailed(new Exception(result.Error ?? "format failed"), cancellationToken);
        }

        private static ToolRequest ParsePrompt(string prompt)
        {
            // Expected simple format: FormatTool Format --language python --code "print('hi')"
            var match = Regex.Match(prompt, @"FormatTool\s+Format\s+(.*)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return new ToolRequest { Operation = "Error", Parameters = new Dictionary<string, object> { ["Error"] = "Could not parse command line" } };
            }

            string args = match.Groups[1].Value;
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string? language = ExtractArg(args, "--language");
            string? code = ExtractArg(args, "--code");

            if (!string.IsNullOrWhiteSpace(language)) dict["language"] = language;
            if (!string.IsNullOrWhiteSpace(code)) dict["code"] = code;

            return new ToolRequest { Operation = "Format", Parameters = dict };
        }

        private static string? ExtractArg(string args, string name)
        {
            var m = Regex.Match(args, $"{Regex.Escape(name)}\\s+(?:\"([^\"]*)\"|([^\\s]+))");
            if (!m.Success) return null;
            return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        }

        private static async Task<ToolResult> HandleAsync(ToolRequest request, CancellationToken ct)
        {
            if (!request.Parameters.TryGetValue("language", out var langObj) || langObj is not string langStr)
            {
                return new ToolResult { IsSuccess = false, Error = "Parameter 'language' is required" };
            }

            if (!request.Parameters.TryGetValue("code", out var codeObj) || codeObj is not string codeStr)
            {
                return new ToolResult { IsSuccess = false, Error = "Parameter 'code' is required" };
            }

            if (!LanguageFormatterFactory.TryGet(langStr, out var formatter))
            {
                return new ToolResult { IsSuccess = false, Error = $"Language '{langStr}' is not supported" };
            }

            if (!formatter.IsAvailable)
            {
                return new ToolResult { IsSuccess = false, Error = $"Formatter for '{langStr}' is not available. Please install the required tool." };
            }

            var (ok, formatted, error) = await formatter.FormatAsync(codeStr, ct);
            return new ToolResult { IsSuccess = ok, Output = formatted, Error = error };
        }

        public async Task<ToolResult> Handle(ToolRequest request)
        {
            return await HandleAsync(request, CancellationToken.None);
        }
    }
} 