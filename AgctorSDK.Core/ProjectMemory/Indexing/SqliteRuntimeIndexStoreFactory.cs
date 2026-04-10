using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.Indexing;

public sealed class SqliteRuntimeIndexStoreFactory : IRuntimeIndexStoreFactory
{
    public IRuntimeIndexStore Create(LoadedProjectContext ctx)
    {
        return new SqliteRuntimeIndexStore(() => RuntimePaths.SqliteDatabaseFile(ctx));
    }
}
