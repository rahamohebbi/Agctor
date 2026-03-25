using AgctorSDK.Core.Tools.Build;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Core.Tests.Tools;

/// <summary>
/// Validates <see cref="DotNetWorkspaceBuild"/> against a real SDK layout (restore + multi-project build).
/// </summary>
public class DotNetWorkspaceBuildTests
{
    [Fact]
    public async Task BuildAsync_RestoresAndBuildsSolution_WithTestProjectUnderTestsFolder()
    {
        if (!DotNetWorkspaceBuild.IsDotNetCliAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "agctor-dotnet-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var testsDir = Path.Combine(root, "Tests");
        Directory.CreateDirectory(testsDir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "Demo.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <DefaultItemExcludes>$(DefaultItemExcludes);Tests/**</DefaultItemExcludes>
                  </PropertyGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(
                Path.Combine(root, "Demo.sln"),
                """
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                VisualStudioVersion = 17.0.31903.59
                MinimumVisualStudioVersion = 10.0.40219.1
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Demo", "Demo.csproj", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Demo.Tests", "Tests\Demo.Tests.csproj", "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}"
                EndProject
                Global
                	GlobalSection(SolutionConfigurationPlatforms) = preSolution
                		Debug|Any CPU = Debug|Any CPU
                	EndGlobalSection
                	GlobalSection(ProjectConfigurationPlatforms) = postSolution
                		{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}.Debug|Any CPU.Build.0 = Debug|Any CPU
                		{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                		{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}.Debug|Any CPU.Build.0 = Debug|Any CPU
                	EndGlobalSection
                EndGlobal
                """);

            await File.WriteAllTextAsync(
                Path.Combine(root, "Lib.cs"),
                """
                namespace T;
                public static class Lib
                {
                    public static int Id() => 1;
                }
                """);

            await File.WriteAllTextAsync(
                Path.Combine(testsDir, "Demo.Tests.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <IsPackable>false</IsPackable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
                    <PackageReference Include="xunit" Version="2.5.0" />
                    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.0" />
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\Demo.csproj" />
                  </ItemGroup>
                </Project>
                """);

            await File.WriteAllTextAsync(
                Path.Combine(testsDir, "LibTests.cs"),
                """
                using Xunit;

                namespace T.Tests;

                public class LibTests
                {
                    [Fact]
                    public void Id_ok() => Assert.Equal(1, Lib.Id());
                }
                """);

            var entry = DotNetWorkspaceBuild.FindSolutionOrProject(Path.Combine(root, "Lib.cs"));
            entry.Should().NotBeNull();
            var (ok, _, err) = await DotNetWorkspaceBuild.BuildAsync(entry!);
            ok.Should().BeTrue($"dotnet build failed: {err}");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
