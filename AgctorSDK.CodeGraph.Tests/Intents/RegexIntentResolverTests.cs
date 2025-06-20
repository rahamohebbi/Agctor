using AgctorSDK.CodeGraph.Intents;
using Xunit;

namespace AgctorSDK.CodeGraph.Tests.Intents
{
    public class RegexIntentResolverTests
    {
        [Theory]
        [InlineData("list classes", IntentKind.ListClasses)]
        [InlineData("List files in the project", IntentKind.ListFiles)]
        [InlineData("list methods in Calculator", IntentKind.ListMethods)]
        [InlineData("Calculator lines of code in class", IntentKind.CountLinesClass)]
        [InlineData("Program.cs lines of code", IntentKind.CountLinesFile)]
        public void ShouldResolveCommonPatterns(string prompt, IntentKind expected)
        {
            var resolver = new RegexIntentResolver();
            var res = resolver.Resolve(prompt);
            Assert.True(res.IsSuccess);
            Assert.Equal(expected, res.Kind);
        }
    }
} 