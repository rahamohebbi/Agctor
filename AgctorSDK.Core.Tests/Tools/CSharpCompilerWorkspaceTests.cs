using System.IO;
using AgctorSDK.Core.Tools.LanguageCompilers;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Tools;

/// <summary>
/// Ensures multi-file same-directory compile resolves cross-file types (CoderAgent gate after refactor).
/// </summary>
public class CSharpCompilerWorkspaceTests
{
    [Fact]
    public async Task CompileSameDirectoryWorkspaceAsync_ResolvesBaseClassInSiblingFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agctor-cs-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var basePath = Path.Combine(dir, "Calculator.cs");
            var derivedPath = Path.Combine(dir, "ScientificCalculator.cs");
            await File.WriteAllTextAsync(basePath, """
                namespace DemoApp
                {
                    public class Calculator { public int Add(int a, int b) => a + b; }
                }
                """);
            await File.WriteAllTextAsync(derivedPath, """
                namespace DemoApp
                {
                    public class ScientificCalculator : Calculator
                    {
                        public double Sqrt(double x) => System.Math.Sqrt(x);
                    }
                }
                """);

            var compiler = new CSharpCompiler();
            var (success, _, error) = await compiler.CompileSameDirectoryWorkspaceAsync(derivedPath);

            success.Should().BeTrue($"errors: {error}");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task CompileSameDirectoryWorkspaceAsync_IncludesTestsCs_AndFailsWithoutXunitRefs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agctor-cs-workspace-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "Lib.cs"), """
                namespace DemoApp { public static class Lib { public static int Id() => 1; } }
                """);
            await File.WriteAllTextAsync(Path.Combine(dir, "LibTests.cs"), """
                using Xunit;
                namespace DemoApp.Tests { public class LibTests { [Fact] public void T() => Xunit.Assert.Equal(1, DemoApp.Lib.Id()); } }
                """);

            var compiler = new CSharpCompiler();
            var (success, _, error) = await compiler.CompileSameDirectoryWorkspaceAsync(Path.Combine(dir, "Lib.cs"));

            success.Should().BeFalse("fallback Roslyn includes *Tests.cs and has no NuGet refs for xUnit");
            error.ToLowerInvariant().Should().Contain("xunit");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }
}
