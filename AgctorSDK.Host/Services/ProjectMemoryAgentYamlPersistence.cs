using System.Text.Json;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Yaml;
using AgctorSDK.Host.Models;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Host.Services;

/// <inheritdoc />
public sealed class ProjectMemoryAgentYamlPersistence : IProjectMemoryAgentYamlPersistence
{
    private readonly IOptionsMonitor<ProjectMemoryAgentOptions> _options;
    private readonly IProjectLoader _loader;
    private readonly IProjectMemoryFileService _files;
    private readonly ILogger<ProjectMemoryAgentYamlPersistence> _logger;

    public ProjectMemoryAgentYamlPersistence(
        IOptionsMonitor<ProjectMemoryAgentOptions> options,
        IProjectLoader loader,
        IProjectMemoryFileService files,
        ILogger<ProjectMemoryAgentYamlPersistence> logger)
    {
        _options = options;
        _loader = loader;
        _files = files;
        _logger = logger;
    }

    private string? RootOrNull()
    {
        var r = _options.CurrentValue.ProjectRoot?.Trim();
        return string.IsNullOrEmpty(r) ? null : Path.GetFullPath(r);
    }

    private static object BadRoot() =>
        new { error = "Agctor:ProjectMemory:ProjectRoot is not set. Use Maintenance page or appsettings." };

    private async Task<(LoadedProjectContext? Ctx, PersistenceResult<T>? Err)> TryLoadContextAsync<T>(string root, CancellationToken cancellationToken)
    {
        try
        {
            var ctx = await _loader.LoadAsync(root, cancellationToken).ConfigureAwait(false);
            return (ctx, null);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Project memory directory layout invalid for root {Root}", root);
            return (null, new PersistenceResult<T> { StatusCode = 400, Error = new { error = ex.Message } });
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Project memory file missing for root {Root}", root);
            return (null, new PersistenceResult<T> { StatusCode = 400, Error = new { error = ex.Message } });
        }
    }

    /// <inheritdoc />
    public async Task<PersistenceResult<AgentDetailDto>> GetAgentDetailAsync(string id, CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return new PersistenceResult<AgentDetailDto> { StatusCode = 400, Error = BadRoot() };

        var (ctx, err) = await TryLoadContextAsync<AgentDetailDto>(root, cancellationToken).ConfigureAwait(false);
        if (err != null)
            return err;
        if (ctx == null)
            return new PersistenceResult<AgentDetailDto> { StatusCode = 500, Error = new { error = "Load failed." } };

        var spec = ctx.AgentSpecs.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        if (spec == null)
            return new PersistenceResult<AgentDetailDto> { StatusCode = 404, Error = new { error = "Not found." } };

        var clone = CloneSpec(spec);
        var yaml = ProjectYamlSerializer.Serialize(clone);
        var rel = spec.SourcePath != null ? ProjectMemoryPathSecurity.ToRelativePath(root, spec.SourcePath) : null;
        return new PersistenceResult<AgentDetailDto>
        {
            StatusCode = 200,
            Data = new AgentDetailDto { Spec = clone, RelativePath = rel, YamlPreview = yaml }
        };
    }

    /// <inheritdoc />
    public async Task<PersistenceResult<object>> SaveAgentAsync(
        string id,
        SaveAgentRequestDto body,
        bool createOnly,
        CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return new PersistenceResult<object> { StatusCode = 400, Error = BadRoot() };
        if (body.Spec == null)
            return new PersistenceResult<object> { StatusCode = 400, Error = new { error = "Spec required." } };

        body.Spec.Id = id;
        body.Spec.SourcePath = null;

        var (ctxPre, errPre) = await TryLoadContextAsync<object>(root, cancellationToken).ConfigureAwait(false);
        if (errPre != null)
            return errPre;
        if (ctxPre == null)
            return new PersistenceResult<object> { StatusCode = 500, Error = new { error = "Load failed." } };

        if (createOnly)
        {
            var exists = ctxPre.AgentSpecs.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (exists)
                return new PersistenceResult<object> { StatusCode = 409, Error = new { error = "Agent id already exists." } };
        }

        var yaml = ProjectYamlSerializer.Serialize(body.Spec);

        string relative;
        if (!string.IsNullOrWhiteSpace(body.RelativePath))
            relative = body.RelativePath.Replace('\\', '/').TrimStart('/');
        else
        {
            var existing = ctxPre.AgentSpecs.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing?.SourcePath != null)
                relative = ProjectMemoryPathSecurity.ToRelativePath(root, existing.SourcePath);
            else
                relative = $".agctor/agents/people/{id}.agent.yaml";
        }

        await _files.WriteAsync(root, relative, yaml, cancellationToken).ConfigureAwait(false);
        return new PersistenceResult<object> { StatusCode = 200, Data = new { saved = true, relativePath = relative } };
    }

    /// <inheritdoc />
    public async Task<PersistenceResult<object>> DeleteAgentAsync(string id, CancellationToken cancellationToken)
    {
        var root = RootOrNull();
        if (root == null)
            return new PersistenceResult<object> { StatusCode = 400, Error = BadRoot() };

        var (ctx, err) = await TryLoadContextAsync<object>(root, cancellationToken).ConfigureAwait(false);
        if (err != null)
            return err;
        if (ctx == null)
            return new PersistenceResult<object> { StatusCode = 500, Error = new { error = "Load failed." } };

        var spec = ctx.AgentSpecs.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        if (spec?.SourcePath == null)
            return new PersistenceResult<object> { StatusCode = 404, Error = new { error = "Not found." } };

        var rel = ProjectMemoryPathSecurity.ToRelativePath(root, spec.SourcePath);
        await _files.DeleteAsync(root, rel, cancellationToken).ConfigureAwait(false);
        return new PersistenceResult<object> { StatusCode = 200, Data = new { deleted = true } };
    }

    private static AgentDefinitionSpec CloneSpec(AgentDefinitionSpec s)
    {
        var c = JsonSerializer.Deserialize<AgentDefinitionSpec>(JsonSerializer.Serialize(s)) ?? new AgentDefinitionSpec();
        c.SourcePath = null;
        return c;
    }
}
