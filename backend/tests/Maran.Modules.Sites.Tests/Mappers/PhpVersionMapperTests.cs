using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Mappers;

namespace Maran.Modules.Sites.Tests.Mappers;

/// <summary>
/// Behavioral contract of <see cref="PhpVersionMapper"/>: each default status the agent client can
/// report arrives as the panel status with the same meaning, and the socket directory is dropped.
/// </summary>
public sealed class PhpVersionMapperTests
{
    /// <summary>Every agent client status maps to the panel status that means the same thing.</summary>
    /// <param name="agent">The agent client's status.</param>
    /// <param name="expected">The panel status it must become.</param>
    [Theory]
    [InlineData(AgentPhpDefaultStatus.Default, PhpDefaultStatus.Default)]
    [InlineData(AgentPhpDefaultStatus.NotDefault, PhpDefaultStatus.NotDefault)]
    [InlineData(AgentPhpDefaultStatus.Unknown, PhpDefaultStatus.Unknown)]
    public void Every_agent_status_maps_to_the_panel_status_with_the_same_meaning(
        AgentPhpDefaultStatus agent,
        PhpDefaultStatus expected)
    {
        var mapped = PhpVersionMapper.From([new PhpVersionDto("8.3", "/run/php", agent)]);

        Assert.Equal("8.3", mapped[0].Version);
        Assert.Equal(expected, mapped[0].DefaultStatus);
    }

    /// <summary>A status the mapper does not know is refused rather than guessed.</summary>
    [Fact]
    public void A_status_the_mapper_does_not_know_is_refused_rather_than_guessed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return PhpVersionMapper.From([new PhpVersionDto("8.3", "/run/php", (AgentPhpDefaultStatus)99)]);
        });
    }
}
