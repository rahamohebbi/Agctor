using AgctorSDK.Core.Rag;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Manages local Docker sidecars for external RAG providers (LightRAG, Cognee MCP).
/// Uses the docker CLI — same pattern as <see cref="IActorRuntimeDockerService"/>.
/// </summary>
public interface IRagProviderDockerService
{
    string? ResolveComposeFilePath();
    Task<RagProviderDockerStatus> GetStatusAsync(string providerId, CancellationToken cancellationToken = default);
    Task<RagProviderDockerActionResult> InstallAsync(string providerId, CancellationToken cancellationToken = default);
    Task<RagProviderDockerActionResult> StartAsync(string providerId, CancellationToken cancellationToken = default);
    Task<RagProviderDockerActionResult> StopAsync(string providerId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RagProviderDockerService : IRagProviderDockerService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<RagProviderDockerService> _logger;

    public RagProviderDockerService(IHostEnvironment environment, ILogger<RagProviderDockerService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? ResolveComposeFilePath()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "docker", "rag-providers", "docker-compose.yml")),
            Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "docker", "rag-providers", "docker-compose.yml"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <inheritdoc />
    public async Task<RagProviderDockerStatus> GetStatusAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var canonical = RagProviderIds.Normalize(providerId);
        var serviceName = RagProviderConfigSchema.GetDockerServiceName(canonical);
        var composePath = ResolveComposeFilePath();
        var status = new RagProviderDockerStatus
        {
            ProviderId = canonical,
            ServiceName = serviceName,
            ComposeFilePath = composePath,
            ComposeFileFound = composePath != null
        };

        if (serviceName == null)
        {
            status.State = "not_applicable";
            status.Message = "This provider does not use a local Docker sidecar.";
            return status;
        }

        if (composePath == null)
        {
            status.State = "missing_compose";
            status.Message = "docker/rag-providers/docker-compose.yml was not found.";
            return status;
        }

        if (!await DockerComposeCli.IsDockerAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            status.DockerAvailable = false;
            status.State = "docker_unavailable";
            status.Message = "Docker CLI is not installed or not running.";
            return status;
        }

        status.DockerAvailable = true;
        var ps = await DockerComposeCli.RunComposeAsync(composePath, $"ps -a --format json {serviceName}", cancellationToken)
            .ConfigureAwait(false);
        if (!ps.Success)
        {
            status.State = "error";
            status.Message = ps.StdErr ?? ps.StdOut ?? "Failed to query docker compose.";
            return status;
        }

        var line = DockerComposeCli.FindServiceJsonLine(ps.StdOut, serviceName);
        if (string.IsNullOrWhiteSpace(line))
        {
            status.State = "stopped";
            status.StatusText = "not created";
            status.Message = "No container yet. Click Start or run the up command.";
            return status;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            var root = doc.RootElement;
            status.ContainerId = root.TryGetProperty("ID", out var id) ? id.GetString() : null;
            status.ContainerName = root.TryGetProperty("Name", out var name) ? name.GetString() : null;
            status.State = root.TryGetProperty("State", out var st) ? st.GetString() ?? "unknown" : "unknown";
            status.StatusText = root.TryGetProperty("Status", out var statusProp) ? statusProp.GetString() : null;
            status.Health = root.TryGetProperty("Health", out var h) ? h.GetString() : null;
            status.Message = !string.IsNullOrWhiteSpace(status.StatusText)
                ? status.StatusText
                : status.State.Equals("running", StringComparison.OrdinalIgnoreCase)
                    ? "Container is running."
                    : $"Container state: {status.State}.";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse docker compose ps JSON for RAG service {Service}", serviceName);
            status.State = "unknown";
            status.Message = "Could not parse docker status output.";
        }

        return status;
    }

    /// <inheritdoc />
    public Task<RagProviderDockerActionResult> InstallAsync(string providerId, CancellationToken cancellationToken = default)
        => RunForServiceAsync(providerId, "pull", cancellationToken);

    /// <inheritdoc />
    public Task<RagProviderDockerActionResult> StartAsync(string providerId, CancellationToken cancellationToken = default)
        => RunForServiceAsync(providerId, "up -d", cancellationToken);

    /// <inheritdoc />
    public Task<RagProviderDockerActionResult> StopAsync(string providerId, CancellationToken cancellationToken = default)
        => RunForServiceAsync(providerId, "stop", cancellationToken);

    private async Task<RagProviderDockerActionResult> RunForServiceAsync(
        string providerId,
        string composeArgs,
        CancellationToken cancellationToken)
    {
        var canonical = RagProviderIds.Normalize(providerId);
        var serviceName = RagProviderConfigSchema.GetDockerServiceName(canonical);
        var composePath = ResolveComposeFilePath();
        if (serviceName == null)
        {
            return new RagProviderDockerActionResult
            {
                Success = false,
                Message = "Provider does not support Docker sidecars."
            };
        }

        if (composePath == null)
        {
            return new RagProviderDockerActionResult
            {
                Success = false,
                Message = "Compose file not found."
            };
        }

        if (!await DockerComposeCli.IsDockerAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new RagProviderDockerActionResult
            {
                Success = false,
                Message = "Docker is not available."
            };
        }

        var result = await DockerComposeCli.RunComposeAsync(composePath, $"{composeArgs} {serviceName}", cancellationToken)
            .ConfigureAwait(false);
        return new RagProviderDockerActionResult
        {
            Success = result.Success,
            Message = result.Success
                ? $"Docker {composeArgs} completed for {serviceName}."
                : result.StdErr ?? result.StdOut ?? "Docker command failed.",
            StdOut = result.StdOut,
            StdErr = result.StdErr
        };
    }
}

/// <summary>Shared docker compose CLI helpers for actor runtime and RAG sidecars.</summary>
internal static class DockerComposeCli
{
    internal static async Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("docker", "info --format {{.ServerVersion}}", cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    internal static Task<(bool Success, string? StdOut, string? StdErr)> RunComposeAsync(
        string composePath,
        string args,
        CancellationToken cancellationToken)
    {
        var composeDir = Path.GetDirectoryName(composePath)!;
        return RunProcessAsync("docker", $"compose -f \"{composePath}\" {args}", cancellationToken, composeDir);
    }

    internal static string? FindServiceJsonLine(string? stdout, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("{", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("Service", out var svc)
                    && string.Equals(svc.GetString(), serviceName, StringComparison.OrdinalIgnoreCase))
                    return line;
            }
            catch
            {
                // skip malformed lines
            }
        }

        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith("{", StringComparison.Ordinal));
    }

    private static async Task<(bool Success, string? StdOut, string? StdErr)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            return (false, null, "Failed to start process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode == 0, stdoutTask.Result, stderrTask.Result);
    }
}
