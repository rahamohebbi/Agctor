using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// No-operation implementation of IMetricsCollector that doesn't collect any metrics.
    /// Used when metrics collection is disabled.
    /// </summary>
    public class NoOpMetricsCollector : IMetricsCollector
    {
        /// <summary>
        /// Singleton instance of the NoOpMetricsCollector.
        /// </summary>
        public static readonly NoOpMetricsCollector Instance = new();
        
        private NoOpMetricsCollector() { }
        
        /// <inheritdoc />
        public void IncrementCounter(string name, long value = 1, params KeyValuePair<string, object>[] tags) { }
        
        /// <inheritdoc />
        public void RecordGauge(string name, double value, params KeyValuePair<string, object>[] tags) { }
        
        /// <inheritdoc />
        public void RecordHistogram(string name, double value, params KeyValuePair<string, object>[] tags) { }
        
        /// <inheritdoc />
        public IDisposable TimeOperation(string name, params KeyValuePair<string, object>[] tags)
        {
            return new NoOpTimer();
        }
        
        private class NoOpTimer : IDisposable
        {
            public void Dispose() { }
        }
    }
} 