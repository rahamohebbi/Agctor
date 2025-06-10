using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace AgctorSDK.Core.Utils.ActivityTracking.OpenTelemetry
{
    /// <summary>
    /// Implementation of IActivityTracker that uses OpenTelemetry for distributed tracing.
    /// </summary>
    public class OpenTelemetryActivityTracker : IActivityTracker
    {
        private readonly Tracer _tracer;
        private readonly string _sourceName;

        /// <summary>
        /// Initializes a new instance of the OpenTelemetryActivityTracker class.
        /// </summary>
        /// <param name="tracerProvider">The OpenTelemetry tracer provider.</param>
        /// <param name="sourceName">The name of the trace source (defaults to "Agctor").</param>
        public OpenTelemetryActivityTracker(TracerProvider tracerProvider, string sourceName = "Agctor")
        {
            _sourceName = sourceName;
            _tracer = tracerProvider.GetTracer(sourceName);
        }

        /// <inheritdoc/>
        public IActivityScope StartActivity(string name, IReadOnlyDictionary<string, string>? context = null)
        {
            var spanContext = default(SpanContext);
            
            // Extract parent context if available
            if (context != null && 
                context.TryGetValue("trace-id", out var traceIdStr) &&
                context.TryGetValue("span-id", out var spanIdStr))
            {
                var traceId = ActivityTraceId.CreateFromString(traceIdStr.AsSpan());
                var spanId = ActivitySpanId.CreateFromString(spanIdStr.AsSpan());
                
                spanContext = new SpanContext(
                    traceId,
                    spanId,
                    ActivityTraceFlags.Recorded,
                    isRemote: true);
            }
            
            // Start a new span with the parent context if available
            var span = _tracer.StartSpan(name, SpanKind.Internal, spanContext);
            return new OpenTelemetryActivityScope(span);
        }

        /// <inheritdoc/>
        public void PropagateContext(IDictionary<string, string> headers)
        {
            if (Activity.Current != null)
            {
                // Directly set the headers without using Inject
                headers["trace-id"] = Activity.Current.TraceId.ToString();
                headers["span-id"] = Activity.Current.SpanId.ToString();
                headers["trace-flags"] = Activity.Current.ActivityTraceFlags.ToString();
            }
        }

        /// <inheritdoc/>
        public IDictionary<string, string> ExtractContext()
        {
            var context = new Dictionary<string, string>();
            if (Activity.Current != null)
            {
                context["trace-id"] = Activity.Current.TraceId.ToString();
                context["span-id"] = Activity.Current.SpanId.ToString();
                context["trace-flags"] = Activity.Current.ActivityTraceFlags.ToString();
            }
            return context;
        }
    }
} 