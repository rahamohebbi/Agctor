using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Coding;

namespace AgctorSDK.Core.Tasks;

/// <summary>
/// Task executor that delegates to an <see cref="ICodeGenerator"/> and marks task status based on generation result.
/// </summary>
public sealed class CoderTaskExecutor : ITaskExecutor
{
    private readonly ICodeGenerator _generator;

    public CoderTaskExecutor(ICodeGenerator generator) => _generator = generator;

    public async Task ExecuteAsync(ProjectTask task, CancellationToken cancellationToken = default)
    {
        var result = await _generator.GenerateAsync(task, cancellationToken);
        task.Status = result.Success ? TaskStatus.Completed : TaskStatus.Failed;
    }
} 