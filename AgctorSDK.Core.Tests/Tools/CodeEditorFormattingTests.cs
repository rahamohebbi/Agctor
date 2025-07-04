using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using AgctorSDK.Core.Tools.Implementations;

namespace AgctorSDK.Core.Tests.Tools
{
    public class CodeEditorFormattingTests
    {
        private static string InitialClass =>
            "namespace DemoApp\n" +
            "{\n" +
            "    public static class MathUtils\n" +
            "    {\n" +
            "        public static int Square(int x) => x * x;\n" +
            "        public static int Cube(int x)   => x * x * x;\n" +
            "    }\n" +
            "}\n";

        [Fact]
        public async Task InsertMethod_IsFormattedCorrectly()
        {
            var tmp = Path.GetTempFileName();
            await File.WriteAllTextAsync(tmp, InitialClass);

            var tool = new CodeEditorTool("test");
            var cmd = $"CodeEditorTool InsertIntoFile --path \"{Path.GetFileName(tmp)}\" --content \"public static double Division(double a, double b) {{ return a / b; }}\" --selector \"class:MathUtils\"";
            await tool.ProcessPromptAsync(cmd);

            var updated = await File.ReadAllTextAsync(tmp);
            Assert.Contains("public static double Division(double a, double b)", updated);
            Assert.Contains("return a / b;", updated);
            // Division signature should be indented 8 spaces relative to namespace
            Assert.Contains("        public static double Division", updated);
        }

        [Fact]
        public async Task RemoveMethod_LeavesProperFormatting()
        {
            var tmp = Path.GetTempFileName();
            // Start with class that already has Division formatted correctly
            var initial = InitialClass.Replace("    }", "        public static double Division(double a, double b) { return a / b; }\n    }");
            await File.WriteAllTextAsync(tmp, initial);

            var tool = new CodeEditorTool("test");
            var cmd = $"CodeEditorTool ReplaceInFile --path \"{Path.GetFileName(tmp)}\" --content \"\" --selector \"class:MathUtils > method:Division\"";
            await tool.ProcessPromptAsync(cmd);

            var updated = await File.ReadAllTextAsync(tmp);
            Assert.DoesNotContain("Division", updated);
            // Ensure closing brace of class is still indented 4 spaces
            Assert.Contains("    }", updated);
        }
    }
} 