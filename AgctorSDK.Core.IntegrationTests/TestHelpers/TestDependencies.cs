using AgctorSDK.Core.Tools.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AgctorSDK.Core.IntegrationTests.TestHelpers
{
    public static class TestDependencies
    {
        public static Mock<IFileSystem>? MockFileSystem { get; set; }
        public static string OllamaUrl { get; set; } = "http://localhost:11434";
        public static string TestModel { get; set; } = "mistral";
        public static TestContext? TestContext { get; set; }
    }
} 