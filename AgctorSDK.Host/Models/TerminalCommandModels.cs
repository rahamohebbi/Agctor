namespace AgctorSDK.Host.Models;

/// <summary>View model for the reusable terminal command panel ViewComponent.</summary>
public sealed class TerminalCommandPanelModel
{
    public string ComponentId { get; set; } = null!;
    public string Title { get; set; } = "Terminal command";
    public string? Description { get; set; }
    /// <summary>Context for preset lookup (e.g. actor runtime id: Orleans).</summary>
    public string? ContextKey { get; set; }
    public string ContextType { get; set; } = "actor-runtime";
    public string Command { get; set; } = "";
    public IReadOnlyList<TerminalCommandPresetDto> Presets { get; set; } = Array.Empty<TerminalCommandPresetDto>();
}

public sealed class TerminalCommandPresetDto
{
    public string Id { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Command { get; set; } = null!;
}

public sealed class RunTerminalCommandRequestDto
{
    public string Command { get; set; } = null!;
    public string? ContextKey { get; set; }
    public string? ContextType { get; set; }
}

public sealed class RunTerminalCommandResponseDto
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Message { get; set; } = null!;
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
}

public sealed class TerminalCommandPresetsResponseDto
{
    public IReadOnlyList<TerminalCommandPresetDto> Presets { get; set; } = Array.Empty<TerminalCommandPresetDto>();
    public string? DefaultCommand { get; set; }
}
