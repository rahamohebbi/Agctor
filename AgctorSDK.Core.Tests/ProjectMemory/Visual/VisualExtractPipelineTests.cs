using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Processing;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Actors;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

[TestClass]
public sealed class VisualExtractPipelineTests
{
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAb/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAGfAP/Z");

    private string _root = "";
    private ServiceProvider? _provider;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "agctor-visual-extract-" + Guid.NewGuid().ToString("N"));
        var sampleRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));
        CopyDir(sampleRoot, _root);

        var services = new ServiceCollection();
        services.Configure<ProjectMemoryAgentOptions>(o => o.ProjectRoot = _root);
        services.Configure<VisualStorageOptions>(o => o.Provider = "file");
        services.Configure<LlmVisionOptions>(o => o.VisualTokenBudget = 256);
        services.AddSingleton<IBlobStore, FileSystemBlobStore>();
        services.AddSingleton<IOllamaVisionChatClient, FakeVisionClient>();
        services.AddSingleton<VisualAssetCatalogStore>();
        services.AddSingleton<IProjectLoader, ProjectLoader>();
        services.AddSingleton<IMemoryIntentProcessor, MemoryIntentProcessor>();
        services.AddSingleton<IGenericInboxStore, GenericInboxStore>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<VisualPipelineService>();
        services.AddSingleton<IVisualPipelineService>(sp => sp.GetRequiredService<VisualPipelineService>());
        _provider = services.BuildServiceProvider();
        ProjectMemoryServiceAccessor.Initialize(_provider);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _provider?.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task Extract_routes_miss_to_generic_inbox()
    {
        var blobs = _provider!.GetRequiredService<IBlobStore>() as FileSystemBlobStore;
        blobs.Should().NotBeNull();
        var catalog = _provider.GetRequiredService<VisualAssetCatalogStore>();
        const string assetId = "extract-test";
        await blobs!.WriteObjectAsync("agctor-visual", "proj/people/x.jpg", new MemoryStream(TinyJpeg), CancellationToken.None);

        await catalog.SaveAsync(
            _root,
            "people",
            new VisualAssetRecord
            {
                AssetId = assetId,
                ScenarioId = "people",
                State = VisualAssetStates.Uploaded,
                Subjects = { new VisualAssetSubject { EntityKey = "raha", Role = "primary" } },
                Storage = new VisualAssetStorageRef
                {
                    Bucket = "agctor-visual",
                    Key = "proj/people/x.jpg",
                    ContentType = "image/jpeg",
                    Bytes = TinyJpeg.Length
                }
            },
            CancellationToken.None);

        var pipeline = _provider.GetRequiredService<IVisualPipelineService>();
        var result = await pipeline.ExtractAsync(new VisualExtractRequest
        {
            ProjectRoot = _root,
            ScenarioId = "people",
            AssetId = assetId
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ProposalCount.Should().BeGreaterThan(0);
        result.Record!.State.Should().Be(VisualAssetStates.InboxPending);
        result.Record.Extraction.SceneSummary.Should().Contain("kayak");

        var inbox = _provider.GetRequiredService<IGenericInboxStore>();
        var pending = await inbox.LoadPendingAsync(_root, CancellationToken.None);
        pending.Should().NotBeEmpty();
    }

    private static void CopyDir(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private sealed class FakeVisionClient : IOllamaVisionChatClient
    {
        public Task<OllamaVisionChatResult> ChatAsync(
            string systemPrompt,
            string userText,
            IReadOnlyList<string> base64Images,
            int? numPredict = null,
            CancellationToken cancellationToken = default)
        {
            if (systemPrompt.Contains("memoryIntents", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new OllamaVisionChatResult
                {
                    Success = true,
                    ModelUsed = "fake-vision",
                    Content =
                        """
                        {"sceneSummary":"Raha in a blue kayak with a dog, wearing sunglasses","memoryIntents":[{"entityKey":"raha","knowledgeType":"unknown_visual_fact","attribute":"note","value":"wearing blue shirt","confidence":0.88}]}
                        """
                });
            }

            if (systemPrompt.Contains("Describe what is happening", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new OllamaVisionChatResult
                {
                    Success = true,
                    ModelUsed = "fake-vision",
                    Content = "Raha is paddling a blue kayak on the water with a black dog and sunglasses."
                });
            }

            return Task.FromResult(new OllamaVisionChatResult
            {
                Success = true,
                ModelUsed = "fake-vision",
                Content = """{"entityKeys":["raha"],"confidence":0.91,"rationale":"visible subject","suggestedIntent":"fitness"}"""
            });
        }
    }
}
