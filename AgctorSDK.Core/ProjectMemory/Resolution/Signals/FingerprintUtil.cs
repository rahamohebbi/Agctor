using System.Security.Cryptography;
using System.Text;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// Stable hash for signal <c>inputsFingerprint</c>. Using SHA-256 over a joined, pipe-delimited
/// input string keeps results reproducible across runs and platforms.
/// </summary>
public static class FingerprintUtil
{
    public static string Of(params string?[] parts)
    {
        var joined = string.Join("|", parts ?? System.Array.Empty<string?>());
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(joined ?? string.Empty));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return "sha256:" + sb.ToString();
    }
}
