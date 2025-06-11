namespace AgctorSDK.Core.Utils.Observability.Metrics
{
    /// <summary>
    /// Constants for metric names used throughout the Agctor system.
    /// </summary>
    public static class MetricsConstants
    {
        private const string Prefix = "agctor_";
        
        /// <summary>
        /// Core system metrics related to message processing and actor counts.
        /// </summary>
        public static class Core
        {
            private const string CorePrefix = Prefix + "core_";
            
            // Message throughput metrics
            public const string MessagesProcessed = CorePrefix + "messages_processed_total";
            public const string MessageProcessingTime = CorePrefix + "message_processing_time_ms";
            public const string MessageQueueDepth = CorePrefix + "message_queue_depth";
            public const string MessageSize = CorePrefix + "message_size_bytes";
            public const string MessagesDelivered = CorePrefix + "messages_delivered_total";
            public const string MessageDeliveryTime = CorePrefix + "message_delivery_time_ms";
            public const string MessagesWithResponse = CorePrefix + "messages_with_response_total";
            public const string MessageRoundtripTime = CorePrefix + "message_roundtrip_time_ms";
            
            // Actor lifecycle metrics
            public const string ActorsCreated = CorePrefix + "actors_created_total";
            public const string ActorsDestroyed = CorePrefix + "actors_destroyed_total";
            public const string ActiveActors = CorePrefix + "active_actors";
            public const string ActorsByType = CorePrefix + "actors_by_type";
            public const string ActorCreationTime = CorePrefix + "actor_creation_time_ms";
            public const string ActorInitializationTime = CorePrefix + "actor_initialization_time_ms";
            public const string ActorShutdownTime = CorePrefix + "actor_shutdown_time_ms";
            public const string ActorStopTime = CorePrefix + "actor_stop_time_ms";
            public const string ActorLookups = CorePrefix + "actor_lookups_total";
            public const string ActorRegistrationTime = CorePrefix + "actor_registration_time_ms";
            public const string ActorsRegistered = CorePrefix + "actors_registered_total";
            
            // Runtime metrics
            public const string RuntimeInitializationTime = CorePrefix + "runtime_initialization_time_ms";
            public const string RuntimeShutdownTime = CorePrefix + "runtime_shutdown_time_ms";
            
            // Actor state metrics
            public const string ActorStateSize = CorePrefix + "actor_state_size_bytes";
            public const string ActorMemoryUsage = CorePrefix + "actor_memory_usage_bytes";
        }
        
        /// <summary>
        /// Tool-specific metrics related to tool execution.
        /// </summary>
        public static class Tools
        {
            private const string ToolsPrefix = Prefix + "tools_";
            
            // Tool execution metrics
            public const string ToolExecutionTime = ToolsPrefix + "execution_time_ms";
            public const string ToolInvocations = ToolsPrefix + "invocations_total";
            public const string ToolSuccessRate = ToolsPrefix + "success_rate";
            public const string ToolFailures = ToolsPrefix + "failures_total";
        }
        
        /// <summary>
        /// Tag names used to categorize metrics.
        /// </summary>
        public static class Tags
        {
            // Actor-related tags
            public const string ActorType = "actor_type";
            public const string ActorId = "actor_id";
            public const string ActorCategory = "actor_category";
            
            // Message-related tags
            public const string MessageType = "message_type";
            public const string Status = "status";
            
            // Runtime-related tags
            public const string Runtime = "runtime_type";
            
            // Tool-related tags
            public const string ToolName = "tool_name";
            public const string ToolOperation = "tool_operation";
        }
    }
} 