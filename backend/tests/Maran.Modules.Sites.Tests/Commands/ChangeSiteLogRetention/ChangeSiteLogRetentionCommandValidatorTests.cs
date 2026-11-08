using Maran.Modules.Sites.Commands.ChangeSiteLogRetention;

namespace Maran.Modules.Sites.Tests.Commands.ChangeSiteLogRetention;

/// <summary>Behavioral contract of <see cref="ChangeSiteLogRetentionCommandValidator"/>.</summary>
public sealed class ChangeSiteLogRetentionCommandValidatorTests
{
    /// <summary>The numbers from one to fourteen pass.</summary>
    /// <param name="days">The number of days.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    public void The_numbers_from_one_to_fourteen_pass(int days)
    {
        var result = new ChangeSiteLogRetentionCommandValidator().Validate(Command(days));

        Assert.True(result.IsValid);
    }

    /// <summary>A number outside the range is refused with the retention code.</summary>
    /// <param name="days">The number of days.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(-5)]
    public void A_number_outside_the_range_is_refused_with_the_retention_code(int days)
    {
        var result = new ChangeSiteLogRetentionCommandValidator().Validate(Command(days));

        Assert.Equal("SiteLogRetentionInvalid", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>An empty site id is refused, since the action always stamps one.</summary>
    [Fact]
    public void An_empty_site_id_is_refused()
    {
        var result = new ChangeSiteLogRetentionCommandValidator().Validate(new ChangeSiteLogRetentionCommand(7));

        Assert.False(result.IsValid);
    }

    /// <summary>Builds a command with a site id.</summary>
    /// <param name="days">The number of days.</param>
    /// <returns>The command.</returns>
    private static ChangeSiteLogRetentionCommand Command(int days)
    {
        return new ChangeSiteLogRetentionCommand(days, Guid.NewGuid());
    }
}
