using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Core.Sessions;

/// <summary>
/// Builds a plain-text transcript prefix from session turns for ProjectMemory ingest prompts.
/// </summary>
public static class SessionTranscriptFormatter
{
    /// <summary>User/assistant lines ordered by <see cref="SessionTurn.Sequence"/>.</summary>
    public static string? BuildPrefix(IReadOnlyList<SessionTurn>? turns)
    {
        if (turns == null || turns.Count == 0)
            return null;

        var sb = new StringBuilder();
        foreach (var t in turns.OrderBy(x => x.Sequence))
        {
            if (t.Role is SessionRole.System or SessionRole.Tool)
                continue;

            var label = t.Role == SessionRole.User ? "User" : "Assistant";
            sb.Append(label).Append(": ").Append(t.Content).Append('\n');
        }

        return sb.Length == 0 ? null : sb.ToString();
    }
}
