using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using AgctorSDK.CodeGraph.Llm;

namespace AgctorSDK.CodeGraph.Snippets
{
    /// <summary>
    /// Universal snippet provider that uses an LLM to extract the requested code fragment when no language-specific provider is available.
    /// It also inherits from <see cref="Agent"/> so that heavy LLM calls happen off the caller thread.
    /// </summary>
    public sealed class SnippetResolverAgent : Agent, ISnippetProvider
    {
        private readonly ILlmClient _llm;

        public SnippetResolverAgent(string id, ILlmClient llm) : base(id)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        }

        public bool CanHandle(string filePath) => true; // catch-all fallback

        public string? GetMethodSource(string filePath, string methodName, int maxLines = 120)
        {
            return GetSnippetAsync(filePath, methodName, "method", maxLines).GetAwaiter().GetResult();
        }

        public string? GetClassSource(string filePath, string className, int maxLines = 400)
        {
            return GetSnippetAsync(filePath, className, "class", maxLines).GetAwaiter().GetResult();
        }

        private async Task<string?> GetSnippetAsync(string filePath, string identifier, string kind, int maxLines)
        {
            if (!File.Exists(filePath)) return null;
            var source = await File.ReadAllTextAsync(filePath);
            var prompt = $"You are a helpful developer assistant. From the following {Path.GetFileName(filePath)} source code, return ONLY the {kind} named '{identifier}'. Do not include any other text. Limit to {maxLines} lines.\n```\n{source}\n```";
            var resp = await _llm.CompleteAsync(prompt);
            return string.IsNullOrWhiteSpace(resp) ? null : resp.Trim();
        }
    }

    internal static class SnippetResolverAgentRegistration
    {
        // This method should be called by composition root where the agent is created and registered in runtime.
        public static void Register(SnippetResolverAgent agent)
        {
            SnippetProviderRegistry.Register(agent);
        }
    }
} 