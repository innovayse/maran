using Maran.Modules.Marketplace.Domain.Entities;

namespace Maran.Modules.Marketplace.Tests.Domain.Entities;

/// <summary>The marketplace switch: off until an administrator turns it on, and every change says who and when.</summary>
public sealed class MarketplaceSettingsTests
{
    /// <summary>The instant of a change.</summary>
    private static readonly DateTimeOffset At = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A switch nobody ever touched is off and names no changer.</summary>
    [Fact]
    public void A_switch_nobody_touched_is_off_and_names_no_one()
    {
        var settings = MarketplaceSettings.Off();

        Assert.False(settings.Enabled);
        Assert.Null(settings.ChangedAt);
        Assert.Null(settings.ChangedBy);
        Assert.Equal(MarketplaceSettings.SingletonId, settings.Id);
    }

    /// <summary>Enabling turns it on and records the administrator and the instant.</summary>
    [Fact]
    public void Enabling_turns_it_on_and_records_who_and_when()
    {
        var settings = MarketplaceSettings.Off();
        var administrator = Guid.NewGuid();

        settings.Enable(At, administrator);

        Assert.True(settings.Enabled);
        Assert.Equal(At, settings.ChangedAt);
        Assert.Equal(administrator, settings.ChangedBy);
    }

    /// <summary>Disabling turns it off again and records the later change.</summary>
    [Fact]
    public void Disabling_turns_it_off_again_and_records_the_later_change()
    {
        var settings = MarketplaceSettings.Off();
        settings.Enable(At, Guid.NewGuid());
        var second = Guid.NewGuid();

        settings.Disable(At.AddHours(1), second);

        Assert.False(settings.Enabled);
        Assert.Equal(At.AddHours(1), settings.ChangedAt);
        Assert.Equal(second, settings.ChangedBy);
    }
}
