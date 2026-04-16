using AgctorSDK.Host.Services.ProjectMemory;

namespace AgctorSDK.Host.IntegrationTests;

public sealed class ProjectMemoryGitWorkspaceScannerTests
{
    [Theory]
    [InlineData(" M people/foo.md", " M", "people/foo.md")]
    [InlineData("M  people/foo.md", "M ", "people/foo.md")]
    [InlineData("?? people/new.txt", "??", "people/new.txt")]
    [InlineData("R  old/a.txt -> people/b.txt", "R ", "people/b.txt")]
    public void TryParsePorcelainLine_parses_paths(string line, string expectStatus, string expectPath)
    {
        ProjectMemoryGitWorkspaceScanner.TryParsePorcelainLine(line, out var st, out var p).Should().BeTrue();
        st.Should().Be(expectStatus);
        p.Should().Be(expectPath);
    }

    [Fact]
    public void FindGitRoot_when_inside_repo_points_at_dotgit_parent()
    {
        var start = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var root = ProjectMemoryGitWorkspaceScanner.FindGitRoot(start);
        if (root != null)
            Directory.Exists(Path.Combine(root, ".git")).Should().BeTrue();
    }
}
