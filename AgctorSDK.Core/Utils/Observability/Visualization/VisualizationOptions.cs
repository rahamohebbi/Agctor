namespace AgctorSDK.Core.Utils.Observability.Visualization
{
    /// <summary>
    /// Options for configuring the visualization service.
    /// </summary>
    public class VisualizationOptions
    {
        /// <summary>
        /// Gets or sets the type of trace viewer to use.
        /// </summary>
        public TraceViewerType TraceViewerType { get; set; } = TraceViewerType.None;
        
        /// <summary>
        /// Gets or sets the base URL for the Jaeger trace viewer.
        /// </summary>
        public string JaegerBaseUrl { get; set; } = "http://localhost:16686";
        
        /// <summary>
        /// Gets or sets the base URL for the Zipkin trace viewer.
        /// </summary>
        public string ZipkinBaseUrl { get; set; } = "http://localhost:9411";
    }
    
    /// <summary>
    /// Enumeration of supported trace viewer types.
    /// </summary>
    public enum TraceViewerType
    {
        /// <summary>
        /// No trace viewer.
        /// </summary>
        None,
        
        /// <summary>
        /// Jaeger trace viewer.
        /// </summary>
        Jaeger,
        
        /// <summary>
        /// Zipkin trace viewer.
        /// </summary>
        Zipkin
    }
} 