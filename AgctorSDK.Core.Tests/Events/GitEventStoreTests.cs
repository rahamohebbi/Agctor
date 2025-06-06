using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgctorSDK.Core.Events;
using AgctorSDK.Core.Utils; // For direct GitCliHelper calls if needed, or for asserting its effects
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Events
{
    [TestClass]
    public class GitEventStoreTests
    {
        private readonly List<string> _testRepoPaths = new();
        // Dummy global config setup is removed as GitCliHelper CommitAsync overrides author, making it less reliant on global config.

        private static readonly JsonSerializerOptions _jsonSerializerOptions = new() 
        { 
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public GitEventStoreTests()
        {
            // No longer setting GIT_CONFIG_GLOBAL or GIT_CONFIG_SYSTEM as GitCliHelper.CommitAsync should make it self-contained.
        }

        private GitEventStore CreateStore(string repoName = "test-event-store")
        {
            var uniqueRepoPath = Path.Combine(Path.GetTempPath(), "AgctorSDKTests_CLI", repoName + "_" + Guid.NewGuid().ToString("N"));
            _testRepoPaths.Add(uniqueRepoPath); 
            return new GitEventStore(uniqueRepoPath);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            foreach (var path in _testRepoPaths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        var directory = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
                        foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
                        {
                            info.Attributes = FileAttributes.Normal;
                        }
                        directory.Delete(true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete test directory {path}: {ex.Message}");
                }
            }
            _testRepoPaths.Clear();

            // No longer restoring GIT_CONFIG_GLOBAL or GIT_CONFIG_SYSTEM as they are not set by tests anymore.
            // Dummy global config directory is also not created anymore.
            GC.SuppressFinalize(this);
        }

        [TestMethod]
        public async Task RecordAsync_ShouldCreateFileAndCommitCorrectly_WithCli()
        {
            // Arrange
            var storeName = "cli-event-store-record-test";
            var eventStore = CreateStore(storeName);
            var record = new EventRecord
            {
                EventType = "TestEventCli",
                ActorId = "TestActorCli",
                Metadata = new Dictionary<string, object> { { "data", "sample_cli" } }
            };
            var repoPath = _testRepoPaths.First(p => p.Contains(storeName));

            // Act
            await eventStore.RecordAsync(record);

            // Assert
            Assert.IsTrue(await GitCliHelper.IsGitRepositoryAsync(repoPath), "Repository should be initialized by Git CLI.");

            var expectedEventDirectory = Path.Combine(repoPath, record.Timestamp.ToString("yyyy-MM-dd"));
            var expectedEventFile = Path.Combine(expectedEventDirectory, $"{record.Id}.json");

            Assert.IsTrue(File.Exists(expectedEventFile), "Event JSON file should exist.");

            var fileContent = await File.ReadAllTextAsync(expectedEventFile);
            var deserializedRecord = JsonSerializer.Deserialize<EventRecord>(fileContent, _jsonSerializerOptions);

            Assert.IsNotNull(deserializedRecord);
            Assert.AreEqual(record.Id, deserializedRecord.Id);
            Assert.AreEqual(record.EventType, deserializedRecord.EventType);

            // Check for commit using GitCliHelper to get latest commit details
            var latestCommitHash = await GitCliHelper.GetLatestCommitHashAsync(repoPath);
            Assert.IsNotNull(latestCommitHash);
            
            // To check commit message and author, we would need a GitCliHelper method 
            // like GetCommitDetails(repoPath, commitHash). For now, we assume the commit happened.
            // A simple check is that a HEAD exists.
        }

        [TestMethod]
        public async Task QueryAsync_ShouldReturnMatchingEventsByPromptHash_WithCli()
        {
            var storeName = "cli-event-store-query-test";
            var eventStore = CreateStore(storeName);
            var promptHashToQuery = "testHashCli123";

            var record1 = new EventRecord { Id = Guid.NewGuid().ToString(), EventType = "TypeA_Cli", RelatedPromptHash = promptHashToQuery, Timestamp = DateTime.UtcNow.AddSeconds(-10) };
            var record2 = new EventRecord { Id = Guid.NewGuid().ToString(), EventType = "TypeB_Cli", RelatedPromptHash = "otherHashCli456" };
            var record3 = new EventRecord { Id = Guid.NewGuid().ToString(), EventType = "TypeC_Cli", RelatedPromptHash = promptHashToQuery, Timestamp = DateTime.UtcNow.AddSeconds(-5) };
            
            await eventStore.RecordAsync(record1);
            await eventStore.RecordAsync(record2);
            await eventStore.RecordAsync(record3);

            var results = (await eventStore.QueryAsync(promptHashToQuery)).ToList();

            Assert.IsNotNull(results);
            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results.Select(r => r.Id).ToList(), record1.Id);
            CollectionAssert.Contains(results.Select(r => r.Id).ToList(), record3.Id);
            Assert.AreEqual(record1.Id, results[0].Id);
            Assert.AreEqual(record3.Id, results[1].Id);
        }

        [TestMethod]
        public async Task QueryAsync_WithNonExistentStorePath_ShouldReturnEmptyList_WithCli()
        {
            var storeName = "cli-event-store-nonexistent-path";
            // Do not create the store, just instantiate with a path that won't exist for the query
            var nonExistentRepoPath = Path.Combine(Path.GetTempPath(), "AgctorSDKTests_CLI", storeName + "_" + Guid.NewGuid().ToString("N"));
            var eventStore = new GitEventStore(nonExistentRepoPath); // This will create the directory, but we want to test QueryAsync
            
            // Manually ensure the directory is gone if GitEventStore constructor created it for IsGitRepository check
            if(Directory.Exists(nonExistentRepoPath)) Directory.Delete(nonExistentRepoPath, true);

            var results = await eventStore.QueryAsync("anyHash");
            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count());
        }

        [TestMethod]
        public async Task RecordAsync_WithNullRecord_ShouldThrowArgumentNullException_WithCli()
        {
            var eventStore = CreateStore("cli-event-store-null-record-test");
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => eventStore.RecordAsync(null!));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task RecordAsync_WithInvalidEventId_ShouldThrowArgumentException_WithCli(string invalidId)
        {
            var eventStore = CreateStore("cli-event-store-invalid-id-test");
            var record = new EventRecord { Id = invalidId, EventType = "TestEventCli" };
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => eventStore.RecordAsync(record));
        }
    }
} 