using System;
using System.Collections.Generic;
using System.Linq;

namespace AgctorSDK.Core.Ollama;

/// <summary>
/// Process-wide Ollama URL and default model (Host calls <see cref="ConfigureDefaults"/> at startup; LLMAgent delegates here).
/// </summary>
public static class OllamaRuntimeConfiguration
{
    public const string DefaultOllamaApiUrl = "http://localhost:11434";
    public const string DefaultModel = "mistral";
    public const int DefaultVisionTimeoutSeconds = 300;

    private static readonly object Gate = new();
    private static string _apiUrl = DefaultOllamaApiUrl;
    private static string _model = DefaultModel;
    private static string? _visionModel;
    private static string[] _visionFallbacks = Array.Empty<string>();
    private static int _visionTimeoutSeconds = DefaultVisionTimeoutSeconds;

    /// <summary>Apply configuration from appsettings / dashboard (same semantics as legacy LLMAgent static defaults).</summary>
    public static void ConfigureDefaults(string? ollamaApiUrl, string? defaultModel)
    {
        lock (Gate)
        {
            _apiUrl = string.IsNullOrWhiteSpace(ollamaApiUrl) ? DefaultOllamaApiUrl : ollamaApiUrl.Trim();
            _model = string.IsNullOrWhiteSpace(defaultModel) ? DefaultModel : defaultModel.Trim();
        }
    }

    /// <summary>Vision model + fallbacks for Gemma 4 <c>/api/chat</c> (PRD-023d).</summary>
    public static void ConfigureVision(string? visionModel, string[]? fallbackModels, int? visionTimeoutSeconds)
    {
        lock (Gate)
        {
            _visionModel = string.IsNullOrWhiteSpace(visionModel) ? null : visionModel.Trim();
            _visionFallbacks = fallbackModels?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            if (visionTimeoutSeconds is > 0)
                _visionTimeoutSeconds = visionTimeoutSeconds.Value;
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

    /// <summary>Primary vision model; falls back to <see cref="GetDefaultModel"/> when unset.</summary>
    public static string GetVisionModel()
    {
        lock (Gate)
        {
            return string.IsNullOrWhiteSpace(_visionModel) ? _model : _visionModel;
        }
    }

    public static string[] GetVisionFallbackModels()
    {
        lock (Gate)
        {
            return _visionFallbacks;
        }
    }

    public static int GetVisionTimeoutSeconds()
    {
        lock (Gate)
        {
            return _visionTimeoutSeconds;
        }
    }

    /// <summary>Ordered model list: primary vision model then configured fallbacks (no duplicates).</summary>
    public static string[] GetVisionModelCandidates()
    {
        lock (Gate)
        {
            var primary = string.IsNullOrWhiteSpace(_visionModel) ? _model : _visionModel;
            var list = new List<string> { primary };
            foreach (var fb in _visionFallbacks)
            {
                if (!list.Contains(fb, StringComparer.OrdinalIgnoreCase))
                    list.Add(fb);
            }

            return list.ToArray();
        }
    }

    /// <summary>Base URL with trailing slash for path concatenation (<c>.../api/generate</c>).</summary>
    public static string GetApiUrlWithTrailingSlash()
    {
        var u = GetOllamaApiUrl().TrimEnd('/');
        return u + "/";
    }
}
