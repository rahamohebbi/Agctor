using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Core.Interfaces
{
    /// <summary>
    /// Builds a compact prompt context from transcript history.
    /// </summary>
    public interface ISessionContextComposer
    {
        SessionContextPackage Compose(SessionTranscript transcript, string currentPrompt, SessionMemoryOptions options);
    }
}
