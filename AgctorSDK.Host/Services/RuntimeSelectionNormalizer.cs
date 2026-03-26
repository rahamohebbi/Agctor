using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Normalizes dashboard/API input to factory runtime ids (PRD-012).
/// </summary>
public static class RuntimeSelectionNormalizer
{
    /// <summary>
    /// Maps user input to a canonical factory id; returns false with error when unknown.
    /// </summary>
    public static bool TryNormalize(string? input, IActorRuntimeAdapterFactory factory, out string canonical, out string? error)
    {
        canonical = "";
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "defaultRuntime is required.";
            return false;
        }

        var t = input.Trim();
        if (string.Equals(t, "Proto", StringComparison.OrdinalIgnoreCase))
            t = "Proto.Actor";

        var available = factory.GetAvailableRuntimes().ToList();
        var match = available.FirstOrDefault(a => string.Equals(a, t, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            error = $"Unknown runtime '{input}'. Available: {string.Join(", ", available)}";
            return false;
        }

        canonical = match;
        return true;
    }
}
