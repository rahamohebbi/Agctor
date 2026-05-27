using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace AgctorSDK.Core.ProjectMemory.Visual;

/// <summary>Resize visual blobs for Ollama (max edge pixels) and emit base64 JPEG.</summary>
public static class VisualImageEncoder
{
    public static async Task<string> ToBase64JpegAsync(
        byte[] imageBytes,
        int maxEdgePixels,
        CancellationToken cancellationToken = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            throw new ArgumentException("Image bytes are required.", nameof(imageBytes));

        maxEdgePixels = Math.Clamp(maxEdgePixels, 256, 4096);

        await using var input = new MemoryStream(imageBytes, writable: false);
        using var image = await Image.LoadAsync(input, cancellationToken).ConfigureAwait(false);

        var w = image.Width;
        var h = image.Height;
        var maxEdge = Math.Max(w, h);
        if (maxEdge > maxEdgePixels)
        {
            var scale = maxEdgePixels / (double)maxEdge;
            var nw = Math.Max(1, (int)Math.Round(w * scale));
            var nh = Math.Max(1, (int)Math.Round(h * scale));
            image.Mutate(ctx => ctx.Resize(nw, nh));
        }

        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 85 }, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToBase64String(output.ToArray());
    }
}
