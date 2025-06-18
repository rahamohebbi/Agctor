using System.Threading.Tasks;

namespace AgctorSDK.CodeGraph.Llm
{
    public record LlmOptions(string Model = "phi3-mini-instruct", float Temperature = 0.2f, int MaxTokens = 512);

    public interface ILlmClient
    {
        Task<string> CompleteAsync(string prompt, LlmOptions? options = null);
    }
} 