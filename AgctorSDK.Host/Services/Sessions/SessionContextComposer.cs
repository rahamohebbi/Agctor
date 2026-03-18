using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Host.Services.Sessions
{
    /// <summary>
    /// Deterministically turns session history into a compact prompt context.
    /// </summary>
    public sealed class SessionContextComposer : ISessionContextComposer
    {
        public SessionContextPackage Compose(SessionTranscript transcript, string currentPrompt, SessionMemoryOptions options)
        {
            transcript ??= new SessionTranscript();
            options ??= new SessionMemoryOptions();

            var recent = (transcript.Turns ?? Array.Empty<SessionTurn>())
                .TakeLast(options.RecentTurnWindow <= 0 ? 8 : options.RecentTurnWindow)
                .ToList();

            var summary = transcript.Summary?.Content ?? string.Empty;
            var context = BuildContext(summary, recent, currentPrompt, options.MaxContextChars <= 0 ? 12000 : options.MaxContextChars);

            return new SessionContextPackage
            {
                SessionId = transcript.Session.SessionId,
                CurrentPrompt = currentPrompt ?? string.Empty,
                Summary = summary,
                RecentTurns = recent,
                PromptContext = context
            };
        }

        private static string BuildContext(string summary, IReadOnlyList<SessionTurn> recent, string currentPrompt, int maxChars)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                sb.AppendLine("SESSION SUMMARY:");
                sb.AppendLine(summary.Trim());
                sb.AppendLine();
            }

            if (recent.Count > 0)
            {
                sb.AppendLine("RECENT TURNS:");
                foreach (var turn in recent)
                {
                    var role = turn.Role.ToString().ToLowerInvariant();
                    sb.Append("- ");
                    sb.Append(role);
                    sb.Append(": ");
                    sb.AppendLine(turn.Content?.Trim() ?? string.Empty);
                }
                sb.AppendLine();
            }

            sb.Append("CURRENT REQUEST: ");
            sb.Append(currentPrompt ?? string.Empty);

            var context = sb.ToString();
            if (context.Length <= maxChars)
            {
                return context;
            }

            // Keep the tail to preserve recent conversation and current request.
            return context.Substring(context.Length - maxChars);
        }
    }
}
