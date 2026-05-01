using AgctorSDK.Core.ProjectMemory.Models;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>Maps confidence bands to immediate ask vs deferred review (PRD-019 hybrid policy).</summary>
public static class OutOfSchemaConfirmationPolicy
{
    /// <returns><see langword="null"/> when the fact should be discarded (below review threshold).</returns>
    public static OutOfSchemaDisposition? Classify(double confidence, OutOfSchemaCaptureOptions options)
    {
        if (confidence < options.ReviewQueueMinConfidence)
            return null;

        if (confidence >= options.ImmediateConfirmationMinConfidence)
            return OutOfSchemaDisposition.ImmediateConfirmation;

        return OutOfSchemaDisposition.ReviewQueue;
    }
}
