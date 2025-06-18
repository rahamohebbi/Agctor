using System.Collections.Generic;
using AgctorSDK.CodeGraph.Analyzers.Abstractions;

namespace AgctorSDK.CodeGraph.Messages
{
    public record MethodDescriptor(string ClassName, string MethodName, string FilePath);
    public record UsageLocation(string FilePath, int Line);

    public record FindPublicMethodsMessage(string? ClassFilter = null);
    public record PublicMethodsResult(IReadOnlyCollection<MethodDescriptor> Methods);

    public record SemanticSearchMessage(string Query, int K = 5);
    public record SemanticSearchResult(IReadOnlyCollection<(string ActorId, float Score)> Matches);
} 