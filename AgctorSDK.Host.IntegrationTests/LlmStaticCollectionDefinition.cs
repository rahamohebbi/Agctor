namespace AgctorSDK.Host.IntegrationTests;

/// <summary>
/// Serializes PRD-015 tests that mutate <see cref="AgctorSDK.Core.Agents.LLMAgent"/> static defaults to avoid cross-test races.
/// </summary>
[CollectionDefinition("LlmStatic", DisableParallelization = true)]
public class LlmStaticCollectionDefinition;
