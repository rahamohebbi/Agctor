using System.Threading.Tasks;
using AgctorSDK.CodeGraph.Analyzers.Roslyn;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AgctorSDK.CodeGraph.Tests.Analyzers
{
    [TestClass]
    public class RoslynAnalyzerTests
    {
        private const string SampleCode = @"using System;

namespace Demo
{
    public class Foo
    {
        public void Bar() { }
        private int Baz(int x) => x * 2;
    }
}";

        [TestMethod]
        public async Task RoslynAnalyzer_ShouldParseClassesAndMethods()
        {
            var analyzer = new RoslynCodeAnalyzer();

            var parsed = await analyzer.AnalyzeAsync("Foo.cs", SampleCode);

            Assert.AreEqual(1, parsed.Classes.Count);
            var cls = parsed.Classes[0];
            Assert.AreEqual("Foo", cls.Name);
            Assert.AreEqual(2, cls.Methods.Count);
            CollectionAssert.Contains(cls.Methods.ConvertAll(m => m.Name), "Bar");
            CollectionAssert.Contains(cls.Methods.ConvertAll(m => m.Name), "Baz");
        }
    }
} 