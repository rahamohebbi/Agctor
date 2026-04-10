using System.Collections.Generic;
using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory;

public interface IMemoryIntentProcessor
{
    /// <summary>Validate and route intents to document targets using routing rules + document types.</summary>
    IReadOnlyList<RoutedMemoryIntent> Route(LoadedProjectContext ctx, IReadOnlyList<MemoryIntent> intents, out IReadOnlyList<ValidationIssue> issues);
}
