namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Minimal replica of the options required for initialising OpenTelemetry in Core-only code paths. The authoritative
    /// version with extended settings sits in the Tools assembly alongside dependency-injection helpers, but we define
    /// this duplicate to avoid introducing a build-time dependency from Core to Tools.
    /// </summary>
    public class OpenTelemetryOptions
    {
        public string SourceName { get; set; } = "Agctor";
        public bool EnableZipkinExporter { get; set; }
        public string ZipkinEndpoint { get; set; } = "http://localhost:9411/api/v2/spans";
        public bool EnableOtlpExporter { get; set; }
        public string OtlpEndpoint { get; set; } = "http://localhost:4317";
        public bool EnableJaegerExporter { get; set; }
        public string JaegerAgentHost { get; set; } = "localhost";
        public int JaegerAgentPort { get; set; } = 6831;
        public string? JaegerCollectorEndpoint { get; set; }
    }
} 