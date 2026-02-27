using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services;

/// <summary>
/// In-memory holder for the current CodeGraph context (PRD-006). Thread-safe for single writer (scenario), multiple readers (API).
/// </summary>
public class CodeGraphContextAccessor : ICodeGraphContextAccessor
{
    private volatile CodeGraphContextDto? _current;

    public CodeGraphContextDto? GetCurrent() => _current;

    public void SetCurrent(CodeGraphContextDto? context) => _current = context;
}
