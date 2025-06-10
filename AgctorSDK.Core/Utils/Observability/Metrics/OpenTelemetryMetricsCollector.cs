using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// OpenTelemetry implementation of IMetricsCollector.
    /// </summary>
    public class OpenTelemetryMetricsCollector : IMetricsCollector, IDisposable
    {
        private readonly string _meterName;
        private readonly Dictionary<string, object> _counterMetrics = new();
        private readonly Dictionary<string, object> _gaugeMetrics = new();
        private readonly Dictionary<string, object> _histogramMetrics = new();
        
        public OpenTelemetryMetricsCollector(string meterName = "AgctorSDK.Core")
        {
            _meterName = meterName;
        }
        
        public void IncrementCounter(string name, long value = 1, params KeyValuePair<string, object>[] tags)
        {
            // Using OpenTelemetry.Metrics to increment a counter
            // In a real implementation, this would create or reuse a Counter instrument
            
            // Simple implementation just logs the metric for now
            System.Console.WriteLine($"METRIC - Counter: {name}, Value: {value}, Tags: {string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}"))}");
        }
        
        public void RecordGauge(string name, double value, params KeyValuePair<string, object>[] tags)
        {
            // Using OpenTelemetry.Metrics to record a gauge value
            // In a real implementation, this would create or reuse a Gauge instrument
            
            // Simple implementation just logs the metric for now
            System.Console.WriteLine($"METRIC - Gauge: {name}, Value: {value}, Tags: {string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}"))}");
        }
        
        public void RecordHistogram(string name, double value, params KeyValuePair<string, object>[] tags)
        {
            // Using OpenTelemetry.Metrics to record a histogram value
            // In a real implementation, this would create or reuse a Histogram instrument
            
            // Simple implementation just logs the metric for now
            System.Console.WriteLine($"METRIC - Histogram: {name}, Value: {value}, Tags: {string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}"))}");
        }
        
        public IDisposable TimeOperation(string name, params KeyValuePair<string, object>[] tags)
        {
            return new TimedOperation(this, name, tags);
        }
        
        public void Dispose()
        {
            // Clean up any resources if needed
            GC.SuppressFinalize(this);
        }
        
        private class TimedOperation : IDisposable
        {
            private readonly Stopwatch _stopwatch;
            private readonly OpenTelemetryMetricsCollector _collector;
            private readonly string _name;
            private readonly KeyValuePair<string, object>[] _tags;
            
            public TimedOperation(OpenTelemetryMetricsCollector collector, string name, KeyValuePair<string, object>[] tags)
            {
                _collector = collector;
                _name = name;
                _tags = tags;
                _stopwatch = Stopwatch.StartNew();
            }
            
            public void Dispose()
            {
                _stopwatch.Stop();
                _collector.RecordHistogram(_name, _stopwatch.Elapsed.TotalMilliseconds, _tags);
            }
        }
    }
} 