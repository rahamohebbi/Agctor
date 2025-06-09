using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Utils.Logging
{
    [TestClass]
    public class LogRotationAndArchivingTests
    {
        private string _testLogDir = string.Empty;
        private string _testArchiveDir = string.Empty;
        
        [TestInitialize]
        public void Setup()
        {
            // Create unique test directories for each test
            var testId = Guid.NewGuid().ToString();
            _testLogDir = Path.Combine(Path.GetTempPath(), "AgctorLogTests", testId, "logs");
            _testArchiveDir = Path.Combine(Path.GetTempPath(), "AgctorLogTests", testId, "archives");
            
            Directory.CreateDirectory(_testLogDir);
            Directory.CreateDirectory(_testArchiveDir);
        }
        
        [TestCleanup]
        public void Cleanup()
        {
            // Clean up test directories after each test
            try
            {
                var parentDir = Directory.GetParent(_testLogDir);
                if (parentDir != null && Directory.Exists(parentDir.FullName))
                {
                    Directory.Delete(parentDir.FullName, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        
        [TestMethod]
        public async Task SizeBasedRotationWithCompression_CreatesAndCompressesLogFiles()
        {
            // Arrange - Configure logger with size-based rotation and compression
            var options = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                ArchiveDirectory = _testArchiveDir,
                FileName = "test-{date}-{time}.log",
                UseTimestampInFilename = true,
                RotationStrategy = RotationStrategy.Size,
                MaxFileSizeBytes = 4 * 1024, // 4KB - set small for testing
                CompressionStrategy = CompressionStrategy.OnRotation,
                ArchiveDirectoryStructure = ArchiveDirectoryStructure.Flat
            };
            
            // Act - Create a logger and write enough data to trigger multiple rotations
            using (var logger = new FileLogger("RotationTest", options))
            {
                // Generate data that will exceed the max file size multiple times
                // Each line is approximately 100 bytes
                string longMessage = new string('x', 50);
                
                // Write enough messages to generate at least 3 log files
                for (int i = 0; i < 150; i++)
                {
                    logger.Info($"Message {i}: {longMessage}");
                    
                    // Small delay to ensure timestamps are different
                    if (i % 50 == 0)
                    {
                        await Task.Delay(10);
                    }
                }
                
                // Ensure last message is flushed
                logger.Info("Final message");
            }
            
            // Give the background tasks time to complete
            await Task.Delay(500);
            
            // Assert - Verify that:
            // 1. We have a current log file in the log directory
            // 2. We have compressed archives in the archive directory
            
            // Check current log file
            var logFiles = Directory.GetFiles(_testLogDir, "*.log");
            Assert.IsTrue(logFiles.Length >= 1, "Should have at least one active log file");
            
            foreach (var logFile in logFiles)
            {
                // Each log file should exist and have content
                Assert.IsTrue(File.Exists(logFile), $"Log file {logFile} should exist");
                Assert.IsTrue(new FileInfo(logFile).Length > 0, $"Log file {logFile} should have content");
                
                // Verify log file contains expected format
                string content = File.ReadAllText(logFile);
                Assert.IsTrue(content.Contains("[INFO ]"), "Log file should contain formatted log entries");
                Assert.IsTrue(content.Contains("[RotationTest]"), "Log file should contain the category");
            }
            
            // Check archived files
            var archiveFiles = Directory.GetFiles(_testArchiveDir, "*.log.gz");
            Assert.IsTrue(archiveFiles.Length >= 1, "Should have at least one compressed archive");
            
            // Verify that at least one archive is properly compressed
            bool foundValidArchive = false;
            foreach (var archiveFile in archiveFiles)
            {
                try
                {
                    // Try to open and read the GZip file
                    using (var fileStream = File.OpenRead(archiveFile))
                    using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                    using (var reader = new StreamReader(gzipStream, Encoding.UTF8))
                    {
                        string content = reader.ReadToEnd();
                        
                        // Verify it contains expected log entries
                        if (content.Contains("[INFO ]") && content.Contains("Message"))
                        {
                            foundValidArchive = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Failed to read archive file {archiveFile}: {ex.Message}");
                }
            }
            
            Assert.IsTrue(foundValidArchive, "Should have at least one valid compressed archive");
            
            // Cleanup is handled by TestCleanup
        }
        
        [TestMethod]
        public async Task BackgroundWorkerLogging_ProcessesAllMessages()
        {
            // Arrange - Configure logger with background worker
            var options = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "background.log",
                UseTimestampInFilename = false,
                UseBackgroundWorker = true,
                MaxQueueSize = 10000
            };
            
            // Act - Generate a lot of log messages quickly
            const int messageCount = 1000;
            using (var logger = new FileLogger("BackgroundTest", options))
            {
                for (int i = 0; i < messageCount; i++)
                {
                    logger.Info($"Background message {i}");
                }
                
                // Allow background worker time to process
                await Task.Delay(500);
            }
            
            // Additional delay after disposal to ensure processing completes
            await Task.Delay(500);
            
            // Assert
            string logFilePath = Path.Combine(_testLogDir, "background.log");
            Assert.IsTrue(File.Exists(logFilePath), "Log file should exist");
            
            // Count the actual log lines
            string[] logLines = File.ReadAllLines(logFilePath);
            
            // Remove the header line
            int actualMessageCount = logLines.Count(line => line.Contains("Background message"));
            
            // We should have processed all (or nearly all) messages
            // Allow for small discrepancies due to timing
            Assert.IsTrue(actualMessageCount >= messageCount * 0.95, 
                $"Expected at least {messageCount * 0.95} messages, but found {actualMessageCount}");
        }
        
        [TestMethod]
        public void ArchiveDirectoryStructure_OrganizesArchivesByYearMonth()
        {
            // Arrange - Configure logger with hierarchical archive structure
            var options = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                ArchiveDirectory = _testArchiveDir,
                FileName = "test.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.Size,
                MaxFileSizeBytes = 2 * 1024, // 2KB - small for quick rotation
                CompressionStrategy = CompressionStrategy.OnRotation,
                ArchiveDirectoryStructure = ArchiveDirectoryStructure.ByYearMonth
            };
            
            // Act - Log enough data to trigger rotation
            using (var logger = new FileLogger("ArchiveTest", options))
            {
                // Generate data to trigger rotation
                for (int i = 0; i < 100; i++)
                {
                    logger.Info($"Message {i}: {new string('x', 50)}");
                }
            }
            
            // Allow time for rotation and compression
            Thread.Sleep(500);
            
            // Assert - Verify hierarchical directory structure
            int year = DateTime.Now.Year;
            int month = DateTime.Now.Month;
            string yearDir = Path.Combine(_testArchiveDir, year.ToString());
            string monthDir = Path.Combine(yearDir, month.ToString("00"));
            
            // Year directory should exist
            Assert.IsTrue(Directory.Exists(yearDir), "Year directory should exist");
            
            // Month directory should exist
            Assert.IsTrue(Directory.Exists(monthDir), "Month directory should exist");
            
            // Should have archives in the month directory
            var archiveFiles = Directory.GetFiles(monthDir, "*.gz");
            Assert.IsTrue(archiveFiles.Length >= 1, "Should have at least one archive in the month directory");
        }
        
        [TestMethod]
        public void RotationWithStatistics_IncludesLogCountStatistics()
        {
            // Arrange - Configure logger with statistics on rotation
            var options = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "stats.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.Size,
                MaxFileSizeBytes = 4 * 1024, // 4KB
                CompressionStrategy = CompressionStrategy.OnRotation,
                ArchiveDirectory = _testArchiveDir,
                IncludeStatisticsOnRotation = true
            };
            
            // Act - Log messages at different levels to trigger statistics
            using (var logger = new FileLogger("StatsTest", options))
            {
                // Generate logs at different levels
                logger.Trace("This is a trace message");
                logger.Debug("This is a debug message");
                
                // Generate more INFO messages to trigger rotation
                for (int i = 0; i < 100; i++)
                {
                    logger.Info($"Info message {i}: {new string('x', 40)}");
                }
                
                logger.Warning("This is a warning message");
                logger.Error("This is an error message");
                logger.Critical("This is a critical message");
                
                // Generate more logs to trigger rotation again
                for (int i = 0; i < 100; i++)
                {
                    logger.Info($"More info {i}");
                }
            }
            
            // Allow time for rotation and compression
            Thread.Sleep(500);
            
            // Assert - Verify statistics in archived files
            var archiveFiles = Directory.GetFiles(_testArchiveDir, "*.gz");
            Assert.IsTrue(archiveFiles.Length >= 1, "Should have at least one archive");
            
            bool foundStatistics = false;
            foreach (var archiveFile in archiveFiles)
            {
                try
                {
                    // Try to read the compressed file
                    using (var fileStream = File.OpenRead(archiveFile))
                    using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                    using (var reader = new StreamReader(gzipStream))
                    {
                        string content = reader.ReadToEnd();
                        
                        // Look for statistics section
                        if (content.Contains("Log Statistics") && 
                            (content.Contains("Info:") || content.Contains("INFO:")))
                        {
                            foundStatistics = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // Ignore individual file errors, we just need to find one with statistics
                }
            }
            
            Assert.IsTrue(foundStatistics, "Should find statistics in at least one archive file");
        }
    }
} 