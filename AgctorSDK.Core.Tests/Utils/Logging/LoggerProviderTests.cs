using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.Core.Tests.Utils.Logging
{
    [TestClass]
    public class LoggerProviderTests
    {
        private string _testLogDir = string.Empty;
        
        [TestInitialize]
        public void Setup()
        {
            // Create a unique test log directory for each test
            _testLogDir = Path.Combine(Path.GetTempPath(), "AgctorLogTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testLogDir);
            
            // Clear any existing providers from previous tests
            LoggerFactory.ClearProviders();
        }
        
        [TestCleanup]
        public void Cleanup()
        {
            // Clean up test log directory after each test
            try
            {
                if (Directory.Exists(_testLogDir))
                {
                    Directory.Delete(_testLogDir, true);
                }
                
                // Ensure providers are cleared after tests
                LoggerFactory.ClearProviders();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        
        [TestMethod]
        public void FileLoggerProvider_CreatesLoggers_WithCorrectConfiguration()
        {
            // Arrange
            var options = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "provider-test.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.None
            };
            
            var provider = new FileLoggerProvider(options, LogLevel.Info);
            
            // Act
            var logger1 = provider.CreateLogger("Category1");
            var logger2 = provider.CreateLogger("Category2");
            
            // Use the loggers
            logger1.Info("Message from Category1");
            logger2.Info("Message from Category2");
            
            // Assert
            string logFilePath = Path.Combine(_testLogDir, "provider-test.log");
            Assert.IsTrue(File.Exists(logFilePath), "Log file should be created");
            
            string logContent = File.ReadAllText(logFilePath);
            Assert.IsTrue(logContent.Contains("[Category1]"), "Log should contain category1");
            Assert.IsTrue(logContent.Contains("[Category2]"), "Log should contain category2");
            Assert.IsTrue(logContent.Contains("Message from Category1"), "Log should contain message1");
            Assert.IsTrue(logContent.Contains("Message from Category2"), "Log should contain message2");
            
            // Cleanup
            provider.Dispose();
        }
        
        [TestMethod]
        public void LoggerFactory_MultipleProviders_LogsToAllDestinations()
        {
            // Arrange
            var fileOptions = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "multi-test.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.None
            };
            
            var errorFileOptions = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "errors-only.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.None
            };
            
            // Add providers to factory
            LoggerFactory.AddProvider(new FileLoggerProvider(fileOptions, LogLevel.Info));
            LoggerFactory.AddProvider(new FileLoggerProvider(errorFileOptions, LogLevel.Error));
            
            // Act
            var logger = LoggerFactory.CreateLogger("MultiProviderTest");
            logger.Info("Info message"); // Should go to multi-test.log only
            logger.Error("Error message"); // Should go to both logs
            
            // Assert
            string generalLogPath = Path.Combine(_testLogDir, "multi-test.log");
            string errorLogPath = Path.Combine(_testLogDir, "errors-only.log");
            
            Assert.IsTrue(File.Exists(generalLogPath), "General log file should exist");
            Assert.IsTrue(File.Exists(errorLogPath), "Error log file should exist");
            
            string generalLogContent = File.ReadAllText(generalLogPath);
            string errorLogContent = File.ReadAllText(errorLogPath);
            
            // General log should have both messages
            Assert.IsTrue(generalLogContent.Contains("Info message"), "General log should contain info message");
            Assert.IsTrue(generalLogContent.Contains("Error message"), "General log should contain error message");
            
            // Error log should only have error message
            Assert.IsFalse(errorLogContent.Contains("Info message"), "Error log should not contain info message");
            Assert.IsTrue(errorLogContent.Contains("Error message"), "Error log should contain error message");
        }
        
        [TestMethod]
        public void LoggerFactory_AddFileLogger_ConfiguresCorrectly()
        {
            // Arrange
            var options = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "factory-test.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.None
            };
            
            // Act - use the convenience method
            LoggerFactory.AddFileLogger(options, LogLevel.Warning);
            
            var logger = LoggerFactory.CreateLogger("FactoryTest");
            logger.Info("Info message"); // Should not be logged
            logger.Warning("Warning message"); // Should be logged
            logger.Error("Error message"); // Should be logged
            
            // Assert
            string logPath = Path.Combine(_testLogDir, "factory-test.log");
            Assert.IsTrue(File.Exists(logPath), "Log file should exist");
            
            string logContent = File.ReadAllText(logPath);
            Assert.IsFalse(logContent.Contains("Info message"), "Log should not contain info message");
            Assert.IsTrue(logContent.Contains("Warning message"), "Log should contain warning message");
            Assert.IsTrue(logContent.Contains("Error message"), "Log should contain error message");
        }
        
        [TestMethod]
        public void LoggerFactory_CompositeLogger_ForwardsToMultipleLoggers()
        {
            // Arrange
            var options1 = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "aggregate1.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.None
            };
            
            var options2 = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "aggregate2.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.None
            };
            
            var logger1 = new FileLogger("AggregateTest", options1);
            var logger2 = new FileLogger("AggregateTest", options2);
            
            // Create composite logger using the factory method
            var aggregateLogger = LoggerFactory.CreateCompositeLogger("AggregateTest", new[] { logger1, logger2 });
            
            // Act
            aggregateLogger.Info("Aggregate message");
            
            // Assert
            string logPath1 = Path.Combine(_testLogDir, "aggregate1.log");
            string logPath2 = Path.Combine(_testLogDir, "aggregate2.log");
            
            Assert.IsTrue(File.Exists(logPath1), "First log file should exist");
            Assert.IsTrue(File.Exists(logPath2), "Second log file should exist");
            
            string logContent1 = File.ReadAllText(logPath1);
            string logContent2 = File.ReadAllText(logPath2);
            
            Assert.IsTrue(logContent1.Contains("Aggregate message"), "First log should contain message");
            Assert.IsTrue(logContent2.Contains("Aggregate message"), "Second log should contain message");
            
            // Cleanup
            logger1.Dispose();
            logger2.Dispose();
        }
        
        [TestMethod]
        public void LoggerFactory_ClearProviders_RemovesAllProviders()
        {
            // Arrange
            var options = new FileLoggerOptions
            {
                LogDirectory = _testLogDir,
                FileName = "clear-test.log",
                UseTimestampInFilename = false,
                RotationStrategy = RotationStrategy.None
            };
            
            LoggerFactory.AddFileLogger(options);
            
            // Act - clear providers then log a message
            LoggerFactory.ClearProviders();
            
            var logger = LoggerFactory.CreateLogger("ClearTest");
            logger.Info("This should not be logged to file");
            
            // Assert - should have created a default console logger, not file logger
            string logPath = Path.Combine(_testLogDir, "clear-test.log");
            
            // File might exist from initialization but shouldn't contain our message
            if (File.Exists(logPath))
            {
                string logContent = File.ReadAllText(logPath);
                Assert.IsFalse(logContent.Contains("This should not be logged to file"), 
                    "Log should not contain message after clearing providers");
            }
        }
    }
} 