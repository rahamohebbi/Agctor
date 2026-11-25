using System;
using System.Collections.Generic;
using System.Linq;
namespace AgctorSDK.Core.SceneMemory;

/// <summary>Structured camera movement and framing state tracked per clip for smooth inter-clip transitions.</summary>
public sealed record SceneCameraTrack(
    string Movement,
    string Direction,
    string Speed,
    string Framing,
    string? TransitionGuidance = null)
{
    public bool Valid() =>
        !string.IsNullOrWhiteSpace(Movement) && Movement.Length <= 120 &&
        !string.IsNullOrWhiteSpace(Direction) && Direction.Length <= 240 &&
        !string.IsNullOrWhiteSpace(Speed) && Speed.Length <= 60 &&
        !string.IsNullOrWhiteSpace(Framing) && Framing.Length <= 240 &&
        (TransitionGuidance == null || TransitionGuidance.Length <= 500);
}

/// <summary>Measured global camera motion vector (pan, tilt, zoom, speed, stability) sampled from video frames.</summary>
public sealed record SceneCameraMotionEvidence(
    string Movement,
    double PanX,
    double TiltY,
    double ZoomRate,
    string Speed,
    double Stability,
    bool Consistent);

/// <summary>Fallible visual observations from ordered samples, separate from approved story facts.</summary>
public sealed record SceneBoundaryState(string Status, string Position, string Motion, string HeldObjects,
    string CompletedActions, string Camera,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SceneCameraTrack? CameraTrack = null)
{
    public bool Valid() => Status is "observed" or "uncertain" &&
        new[] { Position, Motion, HeldObjects, CompletedActions, Camera }.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 1000) &&
        (CameraTrack == null || CameraTrack.Valid());
}
public sealed record SceneBoundaryObservation(string ShotId, string RenderKey, DateTimeOffset ObservedAt,
    string Model, IReadOnlyList<double> SampleSeconds, SceneBoundaryState Ending, double? MediaDurationSeconds = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SceneCameraMotionEvidence? OpeningCamera = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SceneCameraMotionEvidence? EndingCamera = null);
