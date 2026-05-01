using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Tools;
using Microsoft.Extensions.Options;

namespace AgctorSDK.Agents.ProjectMemory;

/// <summary>
/// Focused dependency adapter used by ProjectMemory C# agents. It keeps agent
/// logic testable while preserving the existing service-accessor integration.
/// </summary>
public interface IProjectMemoryAgentServices
{
    string? GetProjectRoot();
    Task<LoadedProjectContext> LoadProjectAsync(string root, CancellationToken cancellationToken);
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken);
    Task<IReadOnlyList<EntitySearchHit>> SearchEntitiesAsync(string projectRoot, string? query, CancellationToken cancellationToken);
    Task<string> ReadDocumentAsync(AgentDefinitionSpec spec, string projectRoot, string relativePath, CancellationToken cancellationToken);
    IReadOnlyList<RoutedMemoryIntent> Route(LoadedProjectContext ctx, IReadOnlyList<MemoryIntent> intents, out IReadOnlyList<ValidationIssue> issues);
    Task<IReadOnlyList<EntityRecord>> DiscoverAsync(LoadedProjectContext ctx, string entityWorkspaceRoot, CancellationToken cancellationToken);
    Task<ProjectionResult> ApplyProjectionAsync(EntityRecord entity, IReadOnlyList<RoutedMemoryIntent> intents, CancellationToken cancellationToken);
}

public sealed class ProjectMemoryAgentServices : IProjectMemoryAgentServices
{
    public static readonly IProjectMemoryAgentServices Default = new ProjectMemoryAgentServices();

    public string? GetProjectRoot() =>
        ProjectMemoryServiceAccessor.GetRequiredService<IOptions<ProjectMemoryAgentOptions>>().Value.ProjectRoot;

    public Task<LoadedProjectContext> LoadProjectAsync(string root, CancellationToken cancellationToken) =>
        ProjectMemoryServiceAccessor.GetRequiredService<IProjectLoader>().LoadAsync(root, cancellationToken);

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken) =>
        ProjectMemoryServiceAccessor.GetRequiredService<IProjectMemoryLlmClient>().GenerateAsync(prompt, cancellationToken);

    public Task<IReadOnlyList<EntitySearchHit>> SearchEntitiesAsync(string projectRoot, string? query, CancellationToken cancellationToken) =>
        ProjectMemoryServiceAccessor.GetRequiredService<ProjectMemoryOperations>().SearchEntitiesAsync(projectRoot, query, cancellationToken);

    public Task<string> ReadDocumentAsync(AgentDefinitionSpec spec, string projectRoot, string relativePath, CancellationToken cancellationToken) =>
        ProjectMemoryServiceAccessor.GetRequiredService<ProjectMemoryOperations>().ReadDocumentAsync(spec, projectRoot, relativePath, cancellationToken);

    public IReadOnlyList<RoutedMemoryIntent> Route(LoadedProjectContext ctx, IReadOnlyList<MemoryIntent> intents, out IReadOnlyList<ValidationIssue> issues) =>
        ProjectMemoryServiceAccessor.GetRequiredService<IMemoryIntentProcessor>().Route(ctx, intents, out issues);

    public Task<IReadOnlyList<EntityRecord>> DiscoverAsync(LoadedProjectContext ctx, string entityWorkspaceRoot, CancellationToken cancellationToken) =>
        ProjectMemoryServiceAccessor.GetRequiredService<IEntityRegistry>().DiscoverAsync(ctx, entityWorkspaceRoot, cancellationToken);

    public Task<ProjectionResult> ApplyProjectionAsync(EntityRecord entity, IReadOnlyList<RoutedMemoryIntent> intents, CancellationToken cancellationToken) =>
        ProjectMemoryServiceAccessor.GetRequiredService<IDocumentProjectionService>().ApplyAsync(entity, intents, cancellationToken);
}
