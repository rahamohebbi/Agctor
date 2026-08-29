using Xunit;

// MCP and Kestrel hosts must not start in parallel; overlapping listeners hung GitHub Actions.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
