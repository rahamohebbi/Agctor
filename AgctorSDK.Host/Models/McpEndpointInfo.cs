namespace AgctorSDK.Host.Models
{
    /// <summary>
    /// Holds the host and port the MCP listener is bound to at runtime. The MCP listener sets
    /// these values once it has successfully started so that integration tests or other services
    /// can discover the actual endpoint when 0 (ephemeral) was configured.
    /// </summary>
    public class McpEndpointInfo
    {
        /// <summary>
        /// Host the listener is bound to. Defaults to 127.0.0.1 for local tests.
        /// </summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// Port the listener is bound to. When 0 was configured this will be set to the
        /// dynamically assigned port after <see cref="TcpListener.Start"/>.
        /// </summary>
        public int Port { get; set; }
    }
} 