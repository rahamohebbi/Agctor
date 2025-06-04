using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AgctorSDK.Core.Utils; // Added for GitCliHelper

namespace AgctorSDK.Core.Events
{
    /// <summary>
    /// An implementation of IEventStore that stores event records as JSON files in a Git repository.
    /// Uses Git CLI for repository operations.
    /// </summary>
    public class GitEventStore : IEventStore
    {
        private readonly string _repositoryPath;
        // private readonly string _baseStorePath; // No longer strictly needed if _repositoryPath is full
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new() 
        { 
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Define a default author for commits if not otherwise specified
        private const string DefaultCommitAuthorName = "AgctorSDK";
        private const string DefaultCommitAuthorEmail = "sdk@agctor.ai";

        /// <summary>
        /// Initializes a new instance of the <see cref="GitEventStore"/> class.
        /// </summary>
        /// <param name="baseStorePath">The base path for the event store (e.g., "event-store"). 
        /// This will be created relative to the current directory if not an absolute path.</param>
        public GitEventStore(string baseStorePath = "event-store")
        {
            // _baseStorePath = baseStorePath; // Not storing if _repositoryPath is absolute
            _repositoryPath = Path.GetFullPath(baseStorePath); // Ensure we have an absolute path
            InitializeRepositoryAsync().ConfigureAwait(false).GetAwaiter().GetResult(); // Synchronously initialize for constructor
        }

        private async Task InitializeRepositoryAsync()
        {
            if (!Directory.Exists(_repositoryPath))
            {
                Directory.CreateDirectory(_repositoryPath);
            }

            if (!await GitCliHelper.IsGitRepositoryAsync(_repositoryPath))
            {
                await GitCliHelper.InitAsync(_repositoryPath);
            }
        }

        /// <summary>
        /// Records an event asynchronously by saving it as a JSON file and committing it to the Git repository using Git CLI.
        /// </summary>
        /// <param name="record">The event record to store.</param>
        public async Task RecordAsync(EventRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.Id)) throw new ArgumentException("Event ID cannot be null or whitespace.", nameof(record.Id));

            var dailyEventFolderPath = Path.Combine(_repositoryPath, record.Timestamp.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dailyEventFolderPath); 

            // Make eventFilePath relative to the repository root for Git add command
            var relativeDailyEventFolderPath = Path.Combine(record.Timestamp.ToString("yyyy-MM-dd"));
            var relativeEventFilePath = Path.Combine(relativeDailyEventFolderPath, $"{record.Id}.json");
            var absoluteEventFilePath = Path.Combine(_repositoryPath, relativeEventFilePath);

            var jsonContent = JsonSerializer.Serialize(record, _jsonSerializerOptions);
            await File.WriteAllTextAsync(absoluteEventFilePath, jsonContent);

            // Use GitCliHelper to add and commit
            await GitCliHelper.AddAsync(_repositoryPath, relativeEventFilePath); // Pass relative path for add
            
            var commitMessage = $"Record event: {record.Id} ({record.EventType})";
            if (!string.IsNullOrWhiteSpace(record.RelatedPromptHash))
            {
                commitMessage += $" for prompt: {record.RelatedPromptHash}";
            }

            await GitCliHelper.CommitAsync(_repositoryPath, commitMessage, DefaultCommitAuthorName, DefaultCommitAuthorEmail);
        }

        /// <summary>
        /// Queries events asynchronously from the Git repository based on a related prompt hash.
        /// This implementation iterates through all event files and filters by RelatedPromptHash.
        /// Note: This part does not directly use Git CLI for querying content but relies on file system traversal.
        /// A more advanced Git-based query would involve `git grep` or parsing log outputs, which is more complex.
        /// </summary>
        /// <param name="promptHash">The hash of the prompt to find related events for.</param>
        /// <returns>A collection of event records matching the prompt hash.</returns>
        public Task<IEnumerable<EventRecord>> QueryAsync(string promptHash)
        {
            if (string.IsNullOrWhiteSpace(promptHash)) throw new ArgumentException("Prompt hash cannot be null or whitespace.", nameof(promptHash));

            var matchingEvents = new List<EventRecord>();

            // Check if the repository path itself exists before enumerating
            if (!Directory.Exists(_repositoryPath))
            {
                // Or throw, or return empty, depending on desired behavior for a non-existent store path
                return Task.FromResult(Enumerable.Empty<EventRecord>()); 
            }

            foreach (var dateDir in Directory.EnumerateDirectories(_repositoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                // Skip .git directory
                if (Path.GetFileName(dateDir).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var filePath in Directory.EnumerateFiles(dateDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(filePath);
                        var eventRecord = JsonSerializer.Deserialize<EventRecord>(jsonContent, _jsonSerializerOptions);

                        if (eventRecord != null && eventRecord.RelatedPromptHash == promptHash)
                        {
                            matchingEvents.Add(eventRecord);
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Error deserializing event file {filePath}: {ex.Message}");
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"Error reading event file {filePath}: {ex.Message}");
                    }
                }
            }
            return Task.FromResult(matchingEvents.OrderBy(e => e.Timestamp).AsEnumerable());
        }
    }
} 