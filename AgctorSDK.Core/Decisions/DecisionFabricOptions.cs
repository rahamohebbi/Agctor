namespace AgctorSDK.Core.Decisions
{
    /// <summary>
    /// Binds the "DecisionFabric" configuration section.
    /// </summary>
    public class DecisionFabricOptions
    {
        public const string SectionName = "DecisionFabric";

        public string DefaultProvider { get; set; } = DecisionFabricIds.Laya;
        public DecisionProvidersOptions Providers { get; set; } = new DecisionProvidersOptions();
        public DecisionRoutingOptions Routing { get; set; } = new DecisionRoutingOptions();
    }

    public class DecisionProvidersOptions
    {
        public LayaProviderOptions Laya { get; set; } = new LayaProviderOptions();
        public OpenAiProviderOptions OpenAI { get; set; } = new OpenAiProviderOptions();
        public RuleProviderOptions Rules { get; set; } = new RuleProviderOptions();
    }

    public class LayaProviderOptions
    {
        public bool Enabled { get; set; } = true;
        public string Endpoint { get; set; } = "http://localhost:8080";
        public string DecidePath { get; set; } = "/v1/decisions";
        public bool IsLocal { get; set; } = true;
        public int ExpectedLatencyMs { get; set; } = 40;
        public decimal ExpectedCost { get; set; } = 0m;
        public double ExpectedAccuracy { get; set; } = 0.8;
    }

    /// <summary>
    /// OpenAI chat-completions shape. Point <see cref="Endpoint"/> at any compatible gateway
    /// (including one in front of another vendor) and set <see cref="Name"/> without changing actors.
    /// </summary>
    public class OpenAiProviderOptions
    {
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = DecisionFabricIds.OpenAI;
        public string Endpoint { get; set; } = "https://api.openai.com";
        public string Model { get; set; } = "configured-model";
        public string? ApiKey { get; set; }
        public string ChatPath { get; set; } = "/v1/chat/completions";
        public bool UseJsonResponseFormat { get; set; } = true;
        public bool IsLocal { get; set; } = false;
        public int ExpectedLatencyMs { get; set; } = 800;
        public decimal ExpectedCost { get; set; } = 0.01m;
        public double ExpectedAccuracy { get; set; } = 0.9;
    }

    public class RuleProviderOptions
    {
        public bool Enabled { get; set; } = true;
    }

    public class DecisionRoutingOptions
    {
        public double ConfidenceThreshold { get; set; } = 0.70;
        public bool EnableFallback { get; set; } = true;
        public bool EnableEscalation { get; set; } = true;

        /// <summary>Questions at least this long are treated as needing the LLM.</summary>
        public int ComplexQuestionLength { get; set; } = 280;

        /// <summary>Choice sets at least this large are treated as needing the LLM.</summary>
        public int ComplexOptionCount { get; set; } = 8;
    }
}
