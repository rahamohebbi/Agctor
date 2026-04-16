using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Isolated <c>Agctor:Scenarios:UserFile</c> in temp storage so parallel tests do not race on the
/// repo's <c>Config/agctor-scenarios.user.json</c> (fixes flaky scenario catalog CRUD / flow tests).
/// </summary>
public class AgctorWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _tempDir;
    private readonly string _userCatalogPath;

    public AgctorWebApplicationFactory()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agctor-host-itest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _userCatalogPath = Path.Combine(_tempDir, "agctor-scenarios.user.json");
        File.WriteAllText(_userCatalogPath, "{\"version\":1,\"scenarios\":[]}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agctor:Scenarios:UserFile"] = _userCatalogPath
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Ignore locked files on CI; temp cleanup is best-effort.
        }

        base.Dispose(disposing);
    }
}
