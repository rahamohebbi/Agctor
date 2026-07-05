using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Services.ProjectMemory;
using FluentAssertions;
using Xunit;

namespace AgctorSDK.Host.IntegrationTests;

public class ProjectMemoryPersonaLlmRunnerPromptTests
{
    [Fact]
    public void BuildPlaygroundPrompt_IncludesRecentConversation()
    {
        var spec = new AgentDefinitionSpec
        {
            Id = "person-extractor",
            Role = "extractor",
            Name = "Person Extractor",
            Instructions = new List<string> { "Extract facts." }
        };

        var prior = new List<SessionTurn>
        {
            new() { Sequence = 1, Role = SessionRole.User, Content = "What is Ryan's relation to Raha?" },
            new() { Sequence = 2, Role = SessionRole.Assistant, Content = "Ryan is their child.", AgentId = "person-query" }
        };

        var prompt = ProjectMemoryPersonaLlmRunner.BuildPlaygroundPrompt(
            spec,
            prior,
            "try again");

        prompt.Should().Contain("Conversation so far");
        prompt.Should().Contain("What is Ryan's relation to Raha?");
        prompt.Should().Contain("Latest user message:");
        prompt.Should().Contain("try again");
    }
}
