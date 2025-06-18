using System.Collections.Generic;

namespace AgctorSDK.CodeGraph.Messages
{
    public record TestTask(string ClassName, string MethodName, string SourceFilePath, string TestProjectPath, string TestFilePath);

    public record PlanTestsMessage(Snapshots.SnapshotDiffResult Diff);
    public record TestPlanResult(IReadOnlyCollection<TestTask> Tasks);

    public record ScaffoldTestMessage(TestTask Task);
    public record TestScaffoldedMessage(string FilePath);
} 