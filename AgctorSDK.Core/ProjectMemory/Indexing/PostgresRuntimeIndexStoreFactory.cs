using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Indexing;

public sealed class PostgresRuntimeIndexStoreFactory : IRuntimeIndexStoreFactory
{
    public IRuntimeIndexStore Create(LoadedProjectContext ctx)
    {
        return new PostgresRuntimeIndexStore(() => RuntimePaths.ResolvePostgresConnectionString(ctx));
    }
}
