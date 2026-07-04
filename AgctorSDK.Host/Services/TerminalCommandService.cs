using System.Text.RegularExpressions;
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
}

/// <inheritdoc />
public sealed class TerminalCommandService : ITerminalCommandService
{
    private static readonly Regex UnsafePattern = new(@"[;&|`$<>(){}\\]", RegexOptions.Compiled);
    private static readonly Regex ComposeFilePattern = new(@"-f\s+(""([^""]+)""|(\S+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IHostEnvironment _environment;
    private readonly IActorRuntimeDockerService _docker;
    private readonly ILogger<TerminalCommandService> _logger;

    public TerminalCommandService(
        IHostEnvironment environment,
        IActorRuntimeDockerService docker,
        ILogger<TerminalCommandService> logger)
    {
        _environment = environment;
        _docker = docker;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? ResolveRepoRoot()
    {
        var compose = _docker.ResolveComposeFilePath();
        if (compose == null) return null;
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(compose)!, "..", ".."));
    }

    /// <inheritdoc />
    public string GetComposeRelativePath() => "docker/actor-runtimes/docker-compose.yml";

    /// <inheritdoc />
    public IReadOnlyList<TerminalCommandPresetDto> GetPresets(string contextType, string? contextKey)
    {
        if (!string.Equals(contextType, "actor-runtime", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(contextKey))
        {
            return Array.Empty<TerminalCommandPresetDto>();
        }

        var service = ActorRuntimeConfigSchema.GetDockerServiceName(contextKey);
        if (service == null) return Array.Empty<TerminalCommandPresetDto>();

        var f = GetComposeRelativePath();
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
        return presets.FirstOrDefault()?.Command ?? $"docker compose -f {GetComposeRelativePath()} ps";
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

        var composePath = _docker.ResolveComposeFilePath();
        if (composePath == null)
        {
            error = "Compose file not found on this machine.";
            return false;
        }

        var match = ComposeFilePattern.Match(args);
        if (!match.Success)
        {
            error = "Command must include -f docker/actor-runtimes/docker-compose.yml";
            return false;
        }

        var fileToken = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
        if (!IsAllowedComposePath(fileToken, composePath))
        {
            error = "Compose file path is not allowed.";
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<RunTerminalCommandResponseDto> RunAsync(string command, CancellationToken cancellationToken = default)
    {
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
            var (success, stdout, stderr, exitCode) = await RunProcessAsync("docker", arguments, repoRoot, timeoutCts.Token)
                .ConfigureAwait(false);

            var detail = BuildResultMessage(success, exitCode, stderr);
            return new RunTerminalCommandResponseDto
            {
                Success = success,
                ExitCode = exitCode,
                Message = detail,
                StdOut = stdout,
                StdErr = stderr
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
        var relativeAllowed = GetComposeRelativePath().Replace('\\', '/');

        if (string.Equals(token.Replace('\\', '/'), relativeAllowed, StringComparison.OrdinalIgnoreCase))
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

    private static async Task<(bool Success, string? StdOut, string? StdErr, int ExitCode)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
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

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            return (false, null, "Failed to start process.", -1);

        // Read stdout and stderr in parallel to avoid pipe buffer deadlocks.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode == 0, stdoutTask.Result, stderrTask.Result, process.ExitCode);
    }
}
