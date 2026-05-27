using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

[TestClass]
public sealed class VisualAssetSupervisorActorTests
{
    [TestMethod]
    public async Task Init_and_complete_upload_file_provider()
    {
        var root = Path.Combine(Path.GetTempPath(), "agctor-visual-actor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".agctor"));
        await File.WriteAllTextAsync(
            Path.Combine(root, ".agctor", "project.yaml"),
            "schemaVersion: 1\nprojectId: test-proj\nprojectType: people\n");

        var catalog = new VisualAssetCatalogStore();
        var blobs = new FileSystemBlobStore(Options.Create(new VisualStorageOptions { Provider = "file", MaxUploadBytes = 1024 * 1024 }));
        var actor = new VisualAssetSupervisorActor("test", catalog, blobs, Options.Create(new VisualStorageOptions()));

        try
        {
            await actor.InitializeAsync();
            var initEnv = await actor.ReceiveAsync(
                AgctorEnvelopeBuilder.Request(
                    new VisualAssetInitUploadRequest(root, "person_1", "image/png", 128, null, null),
                    senderId: "test",
                    receiverId: actor.Id,
                    correlationId: Guid.NewGuid().ToString("N")));
            initEnv.GetMessageType().Should().Be(AgctorMessageTypes.Result);
            var init = initEnv.Payload.Should().BeOfType<VisualAssetInitUploadResult>().Subject;
            init.Success.Should().BeTrue();
            init.AssetId.Should().NotBeNullOrWhiteSpace();

            if (blobs is FileSystemBlobStore fs)
            {
                var record = await catalog.LoadAsync(root, "person_1", init.AssetId!, CancellationToken.None);
                record.Should().NotBeNull();
                await fs.WriteObjectAsync(
                    record!.Storage.Bucket,
                    record.Storage.Key,
                    new MemoryStream(new byte[128]),
                    CancellationToken.None);
            }

            var completeEnv = await actor.ReceiveAsync(
                AgctorEnvelopeBuilder.Request(
                    new VisualAssetCompleteUploadRequest(root, "person_1", init.AssetId!, null),
                    senderId: "test",
                    receiverId: actor.Id,
                    correlationId: Guid.NewGuid().ToString("N")));
            completeEnv.GetMessageType().Should().Be(AgctorMessageTypes.Result);
            var complete = completeEnv.Payload.Should().BeOfType<VisualAssetCompleteUploadResult>().Subject;
            complete.Success.Should().BeTrue();
            complete.Asset!.State.Should().Be(VisualAssetStates.Uploaded);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
