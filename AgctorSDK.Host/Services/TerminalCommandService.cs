using System.Text.RegularExpressions;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// Builds displayable terminal commands and runs validated docker compose commands from the dashboard.
/// </summary>
public interface ITerminalCommandService
{
    string? ResolveRepoRoot();
    string GetComposeRelativePath();
    IReadOnlyList<TerminalCommandPresetDto> GetPresets(string contextType, string? contextKey);
    string GetDefaultCommand(string contextType, string? contextKey);
    bool TryValidate(string command, out string? error);
    Task<RunTerminalCommandResponseDto> RunAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a validated command and pushes stdout/stderr chunks as they arrive (for SSE).
    /// Invokes <paramref name="onChunk"/> with channel "stdout" or "stderr".
    /// </summary>
    Task<RunTerminalCommandResponseDto> RunStreamingAsync(
        string command,
        Func<string, string, CancellationToken, Task> onChunk,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class TerminalCommandService : ITerminalCommandService
{
    private static readonly Regex UnsafePattern = new(@"[;&|`$<>(){}\\]", RegexOptions.Compiled);
    private static readonly Regex ComposeFilePattern = new(@"-f\s+(""([^""]+)""|(\S+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IHostEnvironment _environment;
    private readonly IActorRuntimeDockerService _docker;
    private readonly IRagProviderDockerService _ragDocker;
    private readonly ILogger<TerminalCommandService> _logger;

    public TerminalCommandService(
        IHostEnvironment environment,
        IActorRuntimeDockerService docker,
        IRagProviderDockerService ragDocker,
        ILogger<TerminalCommandService> logger)
    {
        _environment = environment;
        _docker = docker;
        _ragDocker = ragDocker;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? ResolveRepoRoot()
    {
        var compose = _docker.ResolveComposeFilePath() ?? _ragDocker.ResolveComposeFilePath();
        if (compose == null) return null;
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(compose)!, "..", ".."));
    }

    /// <inheritdoc />
    public string GetComposeRelativePath() => GetComposeRelativePath("actor-runtime");

    private static string GetComposeRelativePath(string contextType) =>
        string.Equals(contextType, "rag-provider", StringComparison.OrdinalIgnoreCase)
            ? "docker/rag-providers/docker-compose.yml"
            : "docker/actor-runtimes/docker-compose.yml";

    /// <inheritdoc />
    public IReadOnlyList<TerminalCommandPresetDto> GetPresets(string contextType, string? contextKey)
    {
        if (string.IsNullOrWhiteSpace(contextKey))
            return Array.Empty<TerminalCommandPresetDto>();

        if (string.Equals(contextType, "rag-provider", StringComparison.OrdinalIgnoreCase))
            return BuildPresets(contextType, RagProviderConfigSchema.GetDockerServiceName(contextKey));

        if (string.Equals(contextType, "actor-runtime", StringComparison.OrdinalIgnoreCase))
            return BuildPresets(contextType, ActorRuntimeConfigSchema.GetDockerServiceName(contextKey));

        return Array.Empty<TerminalCommandPresetDto>();
    }

    private IReadOnlyList<TerminalCommandPresetDto> BuildPresets(string contextType, string? service)
    {
        if (service == null) return Array.Empty<TerminalCommandPresetDto>();

        var f = GetComposeRelativePath(contextType);
        return new[]
        {
            Preset("start", "Start sidecar", $"docker compose -f {f} up -d {service}"),
            Preset("stop", "Stop sidecar", $"docker compose -f {f} stop {service}"),
            Preset("install", "Pull image", $"docker compose -f {f} pull {service}"),
            Preset("status", "Show status", $"docker compose -f {f} ps {service}"),
            Preset("logs", "Tail logs", $"docker compose -f {f} logs --tail=100 {service}"),
            Preset("down", "Remove container", $"docker compose -f {f} down {service}")
        };
    }

    /// <inheritdoc />
    public string GetDefaultCommand(string contextType, string? contextKey)
    {
        var presets = GetPresets(contextType, contextKey);
        return presets.FirstOrDefault()?.Command
               ?? $"docker compose -f {GetComposeRelativePath(contextType)} ps";
    }

    /// <inheritdoc />
    public bool TryValidate(string command, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(command))
        {
            error = "Command is empty.";
            return false;
        }

        var trimmed = command.Trim();
        if (UnsafePattern.IsMatch(trimmed))
        {
            error = "Command contains disallowed characters (no shell chaining).";
            return false;
        }

        if (!trimmed.StartsWith("docker ", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only docker commands are allowed.";
            return false;
        }

        var args = trimmed["docker ".Length..].TrimStart();
        if (!args.StartsWith("compose ", StringComparison.OrdinalIgnoreCase)
            && !args.StartsWith("compose\t", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only 'docker compose' commands are allowed.";
            return false;
        }

        var match = ComposeFilePattern.Match(args);
        if (!match.Success)
        {
            error = "Command must include -f docker/.../docker-compose.yml";
            return false;
        }

        var fileToken = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
        var composePath = ResolveComposePathForToken(fileToken);
        if (composePath == null)
        {
            error = "Compose file not found on this machine.";
            return false;
        }

        if (!IsAllowedComposePath(fileToken, composePath))
        {
            error = "Compose file path is not allowed.";
            return false;
        }

        return true;
    }

    private string? ResolveComposePathForToken(string fileToken)
    {
        var actor = _docker.ResolveComposeFilePath();
        var rag = _ragDocker.ResolveComposeFilePath();
        if (actor != null && IsAllowedComposePath(fileToken, actor)) return actor;
        if (rag != null && IsAllowedComposePath(fileToken, rag)) return rag;
        return actor ?? rag;
    }

    /// <inheritdoc />
    public async Task<RunTerminalCommandResponseDto> RunAsync(string command, CancellationToken cancellationToken = default)
    {
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        var result = await RunStreamingAsync(
            command,
            (channel, text, _) =>
            {
                if (string.Equals(channel, "stderr", StringComparison.Ordinal))
                    stderr.Append(text);
                else
                    stdout.Append(text);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        result.StdOut = stdout.Length > 0 ? stdout.ToString() : null;
        result.StdErr = stderr.Length > 0 ? stderr.ToString() : result.StdErr;
        return result;
    }

    /// <inheritdoc />
    public async Task<RunTerminalCommandResponseDto> RunStreamingAsync(
        string command,
        Func<string, string, CancellationToken, Task> onChunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onChunk);

        if (!TryValidate(command, out var validationError))
        {
            return new RunTerminalCommandResponseDto
            {
                Success = false,
                ExitCode = -1,
                Message = validationError ?? "Invalid command."
            };
        }

        var repoRoot = ResolveRepoRoot();
        if (repoRoot == null || !Directory.Exists(repoRoot))
        {
            return new RunTerminalCommandResponseDto
            {
                Success = false,
                ExitCode = -1,
                Message = "Could not resolve repository root for command execution."
            };
        }

        var trimmed = command.Trim();
        var arguments = trimmed["docker ".Length..];

        _logger.LogInformation("Running dashboard terminal command in {Root}: docker {Args}", repoRoot, arguments);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

        try
        {
            var (success, exitCode, stderrTail) = await RunProcessStreamingAsync(
                    "docker",
                    arguments,
                    repoRoot,
                    onChunk,
                    timeoutCts.Token)
                .ConfigureAwait(false);

            return new RunTerminalCommandResponseDto
            {
                Success = success,
                ExitCode = exitCode,
                Message = BuildResultMessage(success, exitCode, stderrTail)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RunTerminalCommandResponseDto
            {
                Success = false,
                ExitCode = -1,
                Message = "Command timed out after 10 minutes.",
                StdErr = "The docker process did not finish in time. Try again or run a shorter command (e.g. ps, pull)."
            };
        }
    }

    private static string BuildResultMessage(bool success, int exitCode, string? stderr)
    {
        if (success) return $"Command completed (exit code {exitCode}).";
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            var firstLine = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine))
                return $"Command failed (exit code {exitCode}): {firstLine}";
        }

        return $"Command failed (exit code {exitCode}).";
    }

    private bool IsAllowedComposePath(string token, string allowedAbsolutePath)
    {
        var normalizedAllowed = Path.GetFullPath(allowedAbsolutePath);
        var actorRelative = "docker/actor-runtimes/docker-compose.yml".Replace('\\', '/');
        var ragRelative = "docker/rag-providers/docker-compose.yml".Replace('\\', '/');
        var normalizedToken = token.Replace('\\', '/');

        if (string.Equals(normalizedToken, actorRelative, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToken, ragRelative, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var full = Path.IsPathRooted(token) ? Path.GetFullPath(token) : Path.GetFullPath(Path.Combine(ResolveRepoRoot() ?? ".", token));
            return string.Equals(full, normalizedAllowed, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static TerminalCommandPresetDto Preset(string id, string label, string command) =>
        new() { Id = id, Label = label, Command = command };

    /// <summary>
    /// Pumps stdout/stderr as chunks arrive so the dashboard can stream docker pull progress live.
    /// Returns a short stderr tail for the final status message.
    /// </summary>
    private static async Task<(bool Success, int ExitCode, string? StdErrTail)> RunProcessStreamingAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Func<string, string, CancellationToken, Task> onChunk,
        CancellationToken cancellationToken)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        // Prefer line-based progress when stdout/stderr are redirected (no TTY).
        psi.Environment["COMPOSE_PROGRESS"] = "plain";
        psi.Environment["DOCKER_CLI_HINTS"] = "false";

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
        {
            await onChunk("stderr", "Failed to start process.", cancellationToken).ConfigureAwait(false);
            return (false, -1, "Failed to start process.");
        }

        var stderrTail = new System.Text.StringBuilder();
        var stdoutTask = PumpStreamAsync(process.StandardOutput, "stdout", onChunk, null, cancellationToken);
        var stderrTask = PumpStreamAsync(process.StandardError, "stderr", onChunk, stderrTail, cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var tail = stderrTail.Length > 0 ? stderrTail.ToString() : null;
        return (process.ExitCode == 0, process.ExitCode, tail);
    }

    private static async Task PumpStreamAsync(
        System.IO.StreamReader reader,
        string channel,
        Func<string, string, CancellationToken, Task> onChunk,
        System.Text.StringBuilder? captureTail,
        CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0)
                break;

            var text = new string(buffer, 0, read);
            if (captureTail != null)
            {
                captureTail.Append(text);
                // Keep only the last ~4 KB for the final status line.
                if (captureTail.Length > 4096)
                    captureTail.Remove(0, captureTail.Length - 4096);
            }

            await onChunk(channel, text, cancellationToken).ConfigureAwait(false);
        }
    }
}
