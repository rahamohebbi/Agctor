using System;
using System.Collections.Generic;
using System.IO;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.ProjectMemory.Loading;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.OutOfSchema;
using AgctorSDK.Core.ProjectMemory.Processing;
using AgctorSDK.Core.ProjectMemory.Visual;
using AgctorSDK.Core.ProjectMemory.Visual.Models;
using AgctorSDK.Core.ProjectMemory.Visual.Storage;
using AgctorSDK.Core.Tools.Implementations;
using AgctorSDK.Core.Tools.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.ProjectMemory.Visual;

[TestClass]
public sealed class PersonVisualToolTests
{
    private string _root = "";
    private ServiceProvider? _provider;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "agctor-visual-tools-" + Guid.NewGuid().ToString("N"));
        var sampleRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "people-project"));
        CopyDir(sampleRoot, _root);

        var services = new ServiceCollection();
        services.Configure<ProjectMemoryAgentOptions>(o => o.ProjectRoot = _root);
        services.Configure<VisualStorageOptions>(o =>
        {
            o.Provider = "file";
            o.MaxUploadBytes = 2 * 1024 * 1024;
        });
        services.Configure<LlmVisionOptions>(_ => { });
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IBlobStore, FileSystemBlobStore>();
        services.AddSingleton<IOllamaVisionChatClient, StubVisionClient>();
        services.AddSingleton<IProjectLoader, ProjectLoader>();
        services.AddSingleton<IMemoryIntentProcessor, MemoryIntentProcessor>();
        services.AddSingleton<IGenericInboxStore, GenericInboxStore>();
        services.AddSingleton<VisualPipelineService>();
        services.AddSingleton<IVisualPipelineService>(sp => sp.GetRequiredService<VisualPipelineService>());
        services.AddSingleton<VisualAssetCatalogStore>();
        services.AddSingleton<PersonVisualContextBuilder>();
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
    public async Task Annotate_and_InferFromPrompt_update_catalog()
    {
        var blobs = _provider!.GetRequiredService<IBlobStore>() as FileSystemBlobStore;
        var tinyJpeg = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAb/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAGfAP/Z");
        await blobs!.WriteObjectAsync("b", "k", new MemoryStream(tinyJpeg), CancellationToken.None);

        var catalog = _provider.GetRequiredService<VisualAssetCatalogStore>();
        var record = new VisualAssetRecord
        {
            AssetId = "a1",
            ScenarioId = "people",
            ProjectId = "test-proj",
            State = VisualAssetStates.Uploaded,
            Storage = new VisualAssetStorageRef { Bucket = "b", Key = "k", ContentType = "image/jpeg", Bytes = tinyJpeg.Length }
        };
        await catalog.SaveAsync(_root, "people", record, CancellationToken.None);

        var ingest = new PersonVisualIngestTool("t-ingest");
        var annotate = await ingest.Handle(new ToolRequest
        {
            Operation = "Annotate",
            Parameters = new Dictionary<string, object>
            {
                ["projectRoot"] = _root,
                ["scenarioId"] = "people",
                ["assetId"] = "a1",
                ["userCaption"] = "leg day",
                ["subjects"] = "[{\"entityKey\":\"raha\",\"role\":\"primary\"}]"
            }
        });
        annotate.IsSuccess.Should().BeTrue();

        var infer = await ingest.Handle(new ToolRequest
        {
            Operation = "InferFromPrompt",
            Parameters = new Dictionary<string, object>
            {
                ["projectRoot"] = _root,
                ["scenarioId"] = "people",
                ["assetId"] = "a1",
                ["userMessage"] = "How does raha look?",
                ["focusEntityKey"] = "raha"
            }
        });
        infer.IsSuccess.Should().BeTrue();

        var saved = await catalog.LoadAsync(_root, "people", "a1", CancellationToken.None);
        saved!.Context.UserCaption.Should().Be("leg day");
        saved.Inference.Should().NotBeNull();
        saved.Inference!.EntityKeys.Should().Contain("raha");
        saved.Inference.Source.Should().Be("vision");
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

    private sealed class StubVisionClient : IOllamaVisionChatClient
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
                    ModelUsed = "stub",
                    Content =
                        """{"memoryIntents":[{"entityKey":"raha","knowledgeType":"observation","value":"gym progress visible","confidence":0.8}]}"""
                });
            }

            return Task.FromResult(new OllamaVisionChatResult
            {
                Success = true,
                ModelUsed = "stub",
                Content = """{"entityKeys":["raha"],"confidence":0.9,"rationale":"stub","suggestedIntent":"fitness"}"""
            });
        }
    }

    [TestMethod]
    public async Task ContextTool_BuildContext_returns_appendix()
    {
        var catalog = _provider!.GetRequiredService<VisualAssetCatalogStore>();
        await catalog.SaveAsync(
            _root,
            "people",
            new VisualAssetRecord
            {
                AssetId = "pic1",
                ScenarioId = "people",
                State = VisualAssetStates.Uploaded,
                UploadedAt = DateTimeOffset.UtcNow,
                Subjects = { new VisualAssetSubject { EntityKey = "raha", Role = "primary" } },
                Storage = new VisualAssetStorageRef { Bucket = "b", Key = "k", ContentType = "image/jpeg", Bytes = 1 }
            },
            CancellationToken.None);

        var contextTool = new PersonVisualContextTool("t-ctx");
        var result = await contextTool.Handle(new ToolRequest
        {
            Operation = "BuildContext",
            Parameters = new Dictionary<string, object>
            {
                ["projectRoot"] = _root,
                ["scenarioId"] = "people",
                ["visualIntent"] = "fitness",
                ["userMessage"] = "progress check",
                ["entityKeys"] = "raha",
                ["maxAssets"] = 3
            }
        });

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().NotBeNull();
        result.Output!.ToString().Should().Contain("pic1");
    }

    [TestMethod]
    public async Task BuildContext_includes_scene_summary_in_appendix()
    {
        var catalog = _provider!.GetRequiredService<VisualAssetCatalogStore>();
        await catalog.SaveAsync(
            _root,
            "people",
            new VisualAssetRecord
            {
                AssetId = "pic-scene",
                ScenarioId = "people",
                State = VisualAssetStates.Ready,
                UploadedAt = DateTimeOffset.UtcNow,
                Subjects = { new VisualAssetSubject { EntityKey = "raha", Role = "primary" } },
                Extraction = { SceneSummary = "Raha kayaking with a dog wearing sunglasses", Status = "completed" },
                Storage = new VisualAssetStorageRef { Bucket = "b", Key = "k", ContentType = "image/jpeg", Bytes = 1 }
            },
            CancellationToken.None);

        var builder = _provider.GetRequiredService<PersonVisualContextBuilder>();
        var result = await builder.BuildAsync(
            _root,
            "people",
            "what am I doing in this photo?",
            "general",
            new[] { "raha" },
            maxAssets: 3,
            CancellationToken.None);

        result.Appendix.Should().Contain("scene:");
        result.Appendix.Should().Contain("kayak");
    }

    [TestMethod]
    public async Task ExtractTool_runs_vision_pipeline()
    {
        var blobs = _provider!.GetRequiredService<IBlobStore>() as FileSystemBlobStore;
        var tinyJpeg = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAb/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAGfAP/Z");
        await blobs!.WriteObjectAsync("b", "k2", new MemoryStream(tinyJpeg), CancellationToken.None);

        var catalog = _provider.GetRequiredService<VisualAssetCatalogStore>();
        await catalog.SaveAsync(
            _root,
            "people",
            new VisualAssetRecord
            {
                AssetId = "x1",
                ScenarioId = "people",
                State = VisualAssetStates.Uploaded,
                Subjects = { new VisualAssetSubject { EntityKey = "raha", Role = "primary" } },
                Storage = new VisualAssetStorageRef { Bucket = "b", Key = "k2", ContentType = "image/jpeg", Bytes = tinyJpeg.Length }
            },
            CancellationToken.None);

        var extract = new PersonVisualExtractTool("t-ex");
        var result = await extract.Handle(new ToolRequest
        {
            Operation = "Extract",
            Parameters = new Dictionary<string, object>
            {
                ["projectRoot"] = _root,
                ["scenarioId"] = "people",
                ["assetId"] = "x1"
            }
        });

        result.IsSuccess.Should().BeTrue();
        result.Output!.ToString().Should().Contain("intentCount");

        var saved = await catalog.LoadAsync(_root, "people", "x1", CancellationToken.None);
        saved!.Extraction.Status.Should().Be("completed");
    }
}
