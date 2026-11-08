using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.Policies;

namespace Maran.Modules.Backups.Tests.Domain.Policies;

/// <summary>What a customer may ask of their own schedule, and that it is narrower than the operator's.</summary>
public sealed class CustomerBackupLimitsTests
{
    /// <summary>The customer's ceiling is inside the entity's, so a stored row can always hold it.</summary>
    [Fact]
    public void The_customer_ceiling_is_inside_the_entity_range_and_below_its_maximum()
    {
        Assert.InRange(
            CustomerBackupLimits.MaximumRetainCount,
            BackupSchedule.MinimumRetainCount,
            BackupSchedule.MaximumRetainCount - 1);
    }

    /// <summary>Daily and weekly are the cadences a customer may choose.</summary>
    [Theory]
    [InlineData(BackupFrequency.Daily)]
    [InlineData(BackupFrequency.Weekly)]
    public void Daily_and_weekly_are_allowed(BackupFrequency frequency)
    {
        Assert.True(CustomerBackupLimits.AllowsFrequency(frequency));
        Assert.Contains(frequency, CustomerBackupLimits.AllowedFrequencies());
    }

    /// <summary>A value that is not a member of the enum is not on the allow-list.</summary>
    /// <remarks>
    /// The refusing control for the allow-list: it is a list of members, not "anything defined", so a
    /// number a client invented is refused.
    /// </remarks>
    [Fact]
    public void A_value_outside_the_enum_is_not_allowed()
    {
        Assert.False(CustomerBackupLimits.AllowsFrequency((BackupFrequency)99));
    }
}
