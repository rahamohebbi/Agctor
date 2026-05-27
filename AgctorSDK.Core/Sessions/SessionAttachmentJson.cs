using System.Collections.Generic;
using System.Text.Json;
using AgctorSDK.Core.Sessions.Models;

namespace AgctorSDK.Core.Sessions;

/// <summary>Serialize/deserialize turn attachment envelopes.</summary>
public static class SessionAttachmentJson
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string? Serialize(SessionAttachmentEnvelope? envelope)
    {
        if (envelope == null || envelope.Attachments.Count == 0)
            return null;
        return JsonSerializer.Serialize(envelope, JsonOpts);
    }

    public static SessionAttachmentEnvelope? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<SessionAttachmentEnvelope>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static SessionAttachmentEnvelope FromAssetIds(IEnumerable<string> assetIds)
    {
        var env = new SessionAttachmentEnvelope();
        foreach (var id in assetIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            env.Attachments.Add(new SessionAttachmentRef { AssetId = id.Trim(), State = "uploaded" });
        }

        return env;
    }
}