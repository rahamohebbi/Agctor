using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// Interface for collecting metrics in the Agctor system.
    /// Provides an abstraction over the underlying metrics implementation.
    /// </summary>
    public interface IMetricsCollector
    {
        /// <summary>
        /// Increments a counter metric by the specified value.
        /// </summary>
        /// <param name="name">The name of the counter</param>
        /// <param name="value">The value to increment by (defaults to 1)</param>
        /// <param name="tags">Optional key-value pairs for tagging/dimensioning the metric</param>
        void IncrementCounter(string name, long value = 1, params KeyValuePair<string, object>[] tags);
        
        /// <summary>
        /// Records a gauge metric value.
        /// </summary>
        /// <param name="name">The name of the gauge</param>
        /// <param name="value">The current value to record</param>
        /// <param name="tags">Optional key-value pairs for tagging/dimensioning the metric</param>
        void RecordGauge(string name, double value, params KeyValuePair<string, object>[] tags);
        
        /// <summary>
        /// Records a histogram metric value.
        /// </summary>
        /// <param name="name">The name of the histogram</param>
        /// <param name="value">The value to record in the histogram</param>
        /// <param name="tags">Optional key-value pairs for tagging/dimensioning the metric</param>
        void RecordHistogram(string name, double value, params KeyValuePair<string, object>[] tags);
        
        /// <summary>
        /// Creates a timed operation that will record its duration as a histogram metric when disposed.
        /// </summary>
        /// <param name="name">The name of the histogram for the duration</param>
        /// <param name="tags">Optional key-value pairs for tagging/dimensioning the metric</param>
        /// <returns>An IDisposable that will record the duration when disposed</returns>
        IDisposable TimeOperation(string name, params KeyValuePair<string, object>[] tags);
    }
} 