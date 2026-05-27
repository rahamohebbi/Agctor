using Microsoft.Data.Sqlite;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Services.Sessions;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class SqliteSessionStoreAttachmentsIntegrationTests
{
    [Fact]
    public async Task AppendTurn_persistsAttachmentsJson()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "agctor-attach-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new SqliteSessionStore(dbPath);
            var sessionId = "s-" + Guid.NewGuid().ToString("N");
            await store.CreateSessionAsync(sessionId, "attach test", projectId: null, CancellationToken.None);

            var json =
                "{\"schemaVersion\":\"1.0\",\"attachments\":[{\"assetId\":\"asset-1\",\"kind\":\"image\",\"state\":\"uploaded\"}]}";
            await store.AppendTurnAsync(
                new SessionTurn
                {
                    SessionId = sessionId,
                    Role = SessionRole.User,
                    Content = "",
                    AttachmentsJson = json
                },
                CancellationToken.None);

            var turns = await store.GetTurnsAsync(sessionId, null, CancellationToken.None);
            turns.Should().HaveCount(1);
            turns[0].AttachmentsJson.Should().Be(json);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
