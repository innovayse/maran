using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Domain.Enums;
using PhpExtensionMapper = Maran.Modules.Sites.Mappers.PhpExtensionMapper;

namespace Maran.Modules.Sites.Tests.Mappers;

/// <summary>
/// Behavioral contract of <see cref="PhpExtensionMapper"/>: each availability the agent client can
/// report arrives as the panel's with the same meaning, and a customer's view carries none.
/// </summary>
public sealed class PhpExtensionMapperTests
{
    /// <summary>Every agent client availability maps to the panel's that means the same thing.</summary>
    /// <param name="agent">The agent client's availability.</param>
    /// <param name="expected">The panel availability it must become.</param>
    [Theory]
    [InlineData(PhpExtensionAvailability.Unknown, ExtensionAvailability.Unknown)]
    [InlineData(PhpExtensionAvailability.Available, ExtensionAvailability.Available)]
    [InlineData(PhpExtensionAvailability.DisableNotSupported, ExtensionAvailability.DisableNotSupported)]
    [InlineData(PhpExtensionAvailability.NotPackaged, ExtensionAvailability.NotPackaged)]
    public void Every_agent_availability_maps_to_the_panel_availability_with_the_same_meaning(
        PhpExtensionAvailability agent,
        ExtensionAvailability expected)
    {
        var mapped = PhpExtensionMapper.ForAdministrator(
            new PhpExtensionDto("gd", true, agent),
            "Images (GD)",
            availability => { return $"word for {availability}"; });

        Assert.Equal("gd", mapped.Name);
        Assert.Equal("Images (GD)", mapped.Label);
        Assert.True(mapped.Enabled);
        Assert.Equal(expected, mapped.Availability);
        // The word is asked for the PANEL's availability, so the two cannot name different things.
        Assert.Equal($"word for {expected}", mapped.AvailabilityName);
    }

    /// <summary>A view built for a customer carries no availability whatever the agent reported.</summary>
    /// <param name="agent">The agent client's availability.</param>
    [Theory]
    [InlineData(PhpExtensionAvailability.Available)]
    [InlineData(PhpExtensionAvailability.NotPackaged)]
    public void A_view_built_for_a_customer_carries_no_availability(PhpExtensionAvailability agent)
    {
        var mapped = PhpExtensionMapper.ForCustomer(new PhpExtensionDto("gd", false, agent), "Images (GD)");

        Assert.Null(mapped.Availability);
        Assert.Null(mapped.AvailabilityName);
        Assert.False(mapped.Enabled);
    }

    /// <summary>An availability the mapper does not know is refused rather than guessed.</summary>
    [Fact]
    public void An_availability_the_mapper_does_not_know_is_refused_rather_than_guessed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return PhpExtensionMapper.ForAdministrator(
                new PhpExtensionDto("gd", true, (PhpExtensionAvailability)99),
                "Images (GD)",
                availability => { return availability.ToString(); });
        });
    }
}
