using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Yaml;

namespace AgctorSDK.Core.ProjectMemory.Loading;

/// <summary>
/// Loads <c>.agctor/project.yaml</c>, runtime, schemas, and agent specs.
/// </summary>
public sealed class ProjectLoader : IProjectLoader
{
    public Task<LoadedProjectContext> LoadAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(projectRoot);
        var agctor = Path.Combine(root, ".agctor");
        if (!Directory.Exists(agctor))
            throw new DirectoryNotFoundException($"Missing .agctor under: {root}");

        var projectPath = Path.Combine(agctor, "project.yaml");
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("project.yaml not found.", projectPath);

        var project = ProjectYamlSerializer.DeserializeFromFile<AgctorProjectManifest>(projectPath);
        if (project.SchemaVersion < 1)
            throw new InvalidDataException("project.yaml schemaVersion must be >= 1.");

        var runtimePath = Path.Combine(agctor, "runtime.yaml");
        var runtime = File.Exists(runtimePath)
            ? ProjectYamlSerializer.DeserializeFromFile<AgctorRuntimeManifest>(runtimePath)
            : new AgctorRuntimeManifest();

        var typeKey = project.ProjectType.Trim();
        if (string.IsNullOrEmpty(typeKey))
            throw new InvalidDataException("project.yaml projectType is required.");

        var typeDir = Path.Combine(agctor, "schemas", typeKey);
        if (!Directory.Exists(typeDir))
            throw new DirectoryNotFoundException($"Schema folder not found: {typeDir}");

        var projectTypePath = Path.Combine(typeDir, "project-type.yaml");
        if (!File.Exists(projectTypePath))
            throw new FileNotFoundException("project-type.yaml not found.", projectTypePath);

        var pt = ProjectYamlSerializer.DeserializeFromFile<ProjectTypeDefinition>(projectTypePath);

        string Sibling(string name) => Path.Combine(typeDir, name);

        var entityPath = pt.EntityTypesRef != null
            ? PathResolver.ResolveFromAgctorRoot(agctor, pt.EntityTypesRef)
            : Sibling("entity-types.yaml");
        var docPath = pt.DocumentTypesRef != null
            ? PathResolver.ResolveFromAgctorRoot(agctor, pt.DocumentTypesRef)
            : Sibling("document-types.yaml");
        var routingPath = pt.RoutingRulesRef != null
            ? PathResolver.ResolveFromAgctorRoot(agctor, pt.RoutingRulesRef)
            : Sibling("routing-rules.yaml");
        var workspacePath = pt.WorkspaceSchemaRef != null
            ? PathResolver.ResolveFromAgctorRoot(agctor, pt.WorkspaceSchemaRef)
            : Sibling("workspace-schema.yaml");

        foreach (var p in new[] { entityPath, docPath, routingPath, workspacePath })
        {
            if (!File.Exists(p))
                throw new FileNotFoundException("Schema file missing.", p);
        }

        var bundle = new ProjectTypeBundle
        {
            ProjectType = pt,
            EntityTypes = ProjectYamlSerializer.DeserializeFromFile<EntityTypesSchema>(entityPath),
            DocumentTypes = ProjectYamlSerializer.DeserializeFromFile<DocumentTypesSchema>(docPath),
            Routing = ProjectYamlSerializer.DeserializeFromFile<RoutingRulesSchema>(routingPath),
            Workspace = ProjectYamlSerializer.DeserializeFromFile<WorkspaceSchema>(workspacePath)
        };

        var agentsDir = Path.Combine(agctor, "agents");
        var agentSpecs = new List<AgentDefinitionSpec>();
        if (Directory.Exists(agentsDir))
        {
            foreach (var file in Directory.EnumerateFiles(agentsDir, "*.agent.yaml", SearchOption.AllDirectories))
            {
                var spec = ProjectYamlSerializer.DeserializeFromFile<AgentDefinitionSpec>(file);
                spec.SourcePath = file;
                agentSpecs.Add(spec);
            }
        }

        agentSpecs = agentSpecs.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();

        var ctx = new LoadedProjectContext
        {
            ProjectRoot = root,
            Project = project,
            Runtime = runtime,
            TypeSchema = bundle,
            AgentSpecs = agentSpecs,
            ResolvedSchemaPaths = new ResolvedSchemaPaths
            {
                ProjectTypeYaml = projectTypePath,
                EntityTypesYaml = entityPath,
                DocumentTypesYaml = docPath,
                RoutingRulesYaml = routingPath,
                WorkspaceSchemaYaml = workspacePath
            }
        };

        return Task.FromResult(ctx);
    }
}
