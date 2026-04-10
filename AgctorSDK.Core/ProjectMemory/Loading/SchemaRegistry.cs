using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Loading;

public sealed class SchemaRegistry : ISchemaRegistry
{
    public SchemaRegistry(ProjectTypeBundle bundle)
    {
        Bundle = bundle;
    }

    public ProjectTypeBundle Bundle { get; }
}
