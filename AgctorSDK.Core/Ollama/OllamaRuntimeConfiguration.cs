namespace AgctorSDK.Core.Ollama;

/// <summary>
/// Process-wide Ollama URL and default model (Host calls <see cref="ConfigureDefaults"/> at startup; LLMAgent delegates here).
/// </summary>
public static class OllamaRuntimeConfiguration
{
    public const string DefaultOllamaApiUrl = "http://localhost:11434";
    public const string DefaultModel = "mistral";

    private static readonly object Gate = new();
    private static string _apiUrl = DefaultOllamaApiUrl;
    private static string _model = DefaultModel;

    /// <summary>Apply configuration from appsettings / dashboard (same semantics as legacy LLMAgent static defaults).</summary>
    public static void ConfigureDefaults(string? ollamaApiUrl, string? defaultModel)
    {
        lock (Gate)
        {
            _apiUrl = string.IsNullOrWhiteSpace(ollamaApiUrl) ? DefaultOllamaApiUrl : ollamaApiUrl.Trim();
            _model = string.IsNullOrWhiteSpace(defaultModel) ? DefaultModel : defaultModel.Trim();
        }
    }

    /// <summary>Base URL without trailing slash (e.g. <c>http://localhost:11434</c>).</summary>
    public static string GetOllamaApiUrl()
    {
        lock (Gate)
        {
            return _apiUrl;
        }
    }

    public static string GetDefaultModel()
    {
        lock (Gate)
        {
            return _model;
        }
    }

    /// <summary>Base URL with trailing slash for path concatenation (<c>.../api/generate</c>).</summary>
    public static string GetApiUrlWithTrailingSlash()
    {
        var u = GetOllamaApiUrl().TrimEnd('/');
        return u + "/";
    }
}
