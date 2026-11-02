using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Host.Extensions;
using Maran.Host.HealthChecks;
using Maran.Host.HealthChecks.Enums;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>
/// Pins the spelling of every health state on the wire. The installer and the end-to-end scripts match
/// these words by value, so a renamed enum member or a lost <c>JsonStringEnumMemberName</c> must fail here
/// rather than in an installer run on a server.
/// </summary>
public sealed class HealthWordTests
{
    /// <summary>The wire's own enum converter, as the panel registers it.</summary>
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Every agent state serializes as the word the scripts match.</summary>
    [Theory]
    [InlineData(AgentHealth.Connected, "connected")]
    [InlineData(AgentHealth.Unavailable, "unavailable")]
    [InlineData(AgentHealth.Incompatible, "incompatible")]
    public void An_agent_state_serializes_as_the_word_the_scripts_match(AgentHealth state, string word)
    {
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new PanelHealthReport(state, DatabaseHealth.Reachable), Wire));

        Assert.Equal(word, body.RootElement.GetProperty("agent").GetString());
        Assert.Equal(word, state.Word());
    }

    /// <summary>Every database state serializes as the word the scripts match, snake_case included.</summary>
    [Theory]
    [InlineData(DatabaseHealth.Reachable, "reachable")]
    [InlineData(DatabaseHealth.NoSchema, "no_schema")]
    [InlineData(DatabaseHealth.Unreachable, "unreachable")]
    [InlineData(DatabaseHealth.NotConfigured, "not_configured")]
    public void A_database_state_serializes_as_the_word_the_scripts_match(DatabaseHealth state, string word)
    {
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new PanelHealthReport(AgentHealth.Connected, state), Wire));

        Assert.Equal(word, body.RootElement.GetProperty("database").GetString());
        Assert.Equal(word, state.Word());
    }

    /// <summary>The report carries the two states and no overall status member.</summary>
    [Fact]
    public void The_report_carries_the_two_states_and_no_overall_status_member()
    {
        using var body = JsonDocument.Parse(
            JsonSerializer.Serialize(new PanelHealthReport(AgentHealth.Connected, DatabaseHealth.Reachable), Wire));

        Assert.Equal(new[] { "agent", "database" }, body.RootElement.EnumerateObject().Select(property =>
        {
            return property.Name;
        }).Order().ToArray());
    }
}
