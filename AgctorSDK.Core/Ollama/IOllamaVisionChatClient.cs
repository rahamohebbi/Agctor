using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgctorSDK.Core.Ollama;

/// <summary>Ollama multimodal <c>POST /api/chat</c> with base64 <c>images[]</c> (PRD-023).</summary>
public interface IOllamaVisionChatClient
{
    Task<OllamaVisionChatResult> ChatAsync(
        string systemPrompt,
        string userText,
        IReadOnlyList<string> base64Images,
        int? numPredict = null,
        CancellationToken cancellationToken = default);
}
