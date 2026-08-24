using CodexGateway.Infrastructure.Codex;

namespace CodexGateway.Tests.Codex.Containers;

public sealed class CodexJsonEventTests
{
    [Fact]
    public void Completed_agent_message_is_extracted()
    {
        const string json = "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"hello\"}}";

        Assert.True(ContainerCodexRunner.TryReadAgentText(json, out var text));
        Assert.Equal("hello", text);
    }

    [Fact]
    public void Empty_completed_agent_message_is_still_recognized_as_the_last_message()
    {
        const string json = "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"\"}}";

        Assert.True(ContainerCodexRunner.TryReadAgentText(json, out var text));
        Assert.Empty(text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"type\":\"item.completed\",\"item\":{\"type\":\"reasoning\",\"text\":\"secret\"}}")]
    [InlineData("{\"type\":\"unknown\"}")]
    public void Unknown_or_non_message_output_is_ignored(string line)
    {
        Assert.False(ContainerCodexRunner.TryReadAgentText(line, out _));
    }

    [Theory]
    [InlineData("{\"answer\":\"ok\"}", true)]
    [InlineData("[1,true,null]", true)]
    [InlineData("null", true)]
    [InlineData("not json", false)]
    [InlineData("```json\n{}\n```", false)]
    [InlineData("{} trailing", false)]
    [InlineData("{", false)]
    public void Structured_output_requires_one_complete_json_document(string text, bool expected)
    {
        Assert.Equal(expected, ContainerCodexRunner.IsValidJsonDocument(text));
    }
}
