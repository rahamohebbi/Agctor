using System.Collections.Generic;

namespace AgctorSDK.Core.DependencyInjection
{
    /// <summary>
    /// Core-level configuration options used by various runtime services. This duplicate lives in Core so that we can keep Core independent of the higher-level
    /// dependency-injection helpers that now reside in the Tools/Agents assemblies.
    /// </summary>
    public class AgctorOptions
    {
        /// <summary>
        /// The default name of the runtime implementation to use (e.g. "InMemory", "Orleans").
        /// </summary>
        public string DefaultRuntime { get; set; } = "InMemory";

        /// <summary>
        /// Maximum number of concurrent messages that can be processed by the system.
        /// </summary>
        public int MaxConcurrentMessages { get; set; } = 1000;

        /// <summary>
        /// Default timeout (in milliseconds) applied to message processing if no specific timeout is supplied.
        /// </summary>
        public int DefaultTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Enable extremely verbose logging for debugging.
        /// </summary>
        public bool EnableDetailedLogging { get; set; }

        /// <summary>
        /// Logical environment name (Development, Production, CLI, etc.).
        /// </summary>
        public string Environment { get; set; } = "Development";

        /// <summary>
        /// Bag for any additional custom configuration items.
        /// </summary>
        public Dictionary<string, object> AdditionalProperties { get; set; } = new();
    }
} 