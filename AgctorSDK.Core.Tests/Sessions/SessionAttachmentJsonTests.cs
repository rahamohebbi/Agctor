using AgctorSDK.Core.Sessions;
using AgctorSDK.Core.Sessions.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Sessions;

[TestClass]
public sealed class SessionAttachmentJsonTests
{
    [TestMethod]
    public void SerializeDeserialize_roundTrips()
    {
        var env = new SessionAttachmentEnvelope
        {
            Attachments =
            {
                new SessionAttachmentRef { AssetId = "a1", State = "uploaded", Mime = "image/jpeg" }
            }
        };

        var json = SessionAttachmentJson.Serialize(env);
        Assert.IsNotNull(json);
        var back = SessionAttachmentJson.Deserialize(json);
        Assert.IsNotNull(back);
        Assert.AreEqual(1, back!.Attachments.Count);
        Assert.AreEqual("a1", back.Attachments[0].AssetId);
    }

    [TestMethod]
    public void FromAssetIds_buildsEnvelope()
    {
        var env = SessionAttachmentJson.FromAssetIds(new[] { "x", "y" });
        Assert.AreEqual(2, env.Attachments.Count);
    }
}
