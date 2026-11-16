using System;
using System.Collections.Generic;
using System.Linq;
namespace AgctorSDK.Core.SceneMemory;

/// <summary>Fallible visual observations from ordered samples, separate from approved story facts.</summary>
public sealed record SceneBoundaryState(string Status, string Position, string Motion, string HeldObjects,
    string CompletedActions, string Camera)
{
    public bool Valid() => Status is "observed" or "uncertain" &&
        new[] { Position, Motion, HeldObjects, CompletedActions, Camera }.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 1000);
}
public sealed record SceneBoundaryObservation(string ShotId, string RenderKey, DateTimeOffset ObservedAt,
    string Model, IReadOnlyList<double> SampleSeconds, SceneBoundaryState Ending, double? MediaDurationSeconds = null);
