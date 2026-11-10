using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Tests.Domain.Entities;

/// <summary>The life of one package row: staged, then active or closed, and closed history stays closed.</summary>
public sealed class InstalledPackageTests
{
    /// <summary>The instant the row is staged.</summary>
    private static readonly DateTimeOffset StagedAt = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A fresh package row.</summary>
    /// <returns>A staged row for the module <c>mail</c> at 1.2.0.</returns>
    private static InstalledPackage NewRow()
    {
        return new InstalledPackage(
            Guid.NewGuid(),
            "mail",
            "1.2.0",
            new string('a', 64),
            Guid.NewGuid(),
            "1.1.0",
            StagedAt);
    }

    /// <summary>A newly staged package is staged, not active, and remembers what it replaced.</summary>
    [Fact]
    public void A_newly_staged_package_is_staged_and_remembers_the_version_it_replaced()
    {
        var row = NewRow();

        Assert.Equal(InstalledPackageState.Staged, row.State);
        Assert.Equal("1.1.0", row.PreviousVersion);
        Assert.Equal(StagedAt, row.StateChangedAt);
    }

    /// <summary>The row keeps the catalogue name it was staged with; a row staged without one has none.</summary>
    [Fact]
    public void A_row_keeps_the_catalogue_name_it_was_staged_with()
    {
        var named = new InstalledPackage(
            Guid.NewGuid(), "mail", "1.2.0", new string('a', 64), Guid.NewGuid(), null, StagedAt, "Mail server");

        Assert.Equal("Mail server", named.DisplayName);
        Assert.Null(NewRow().DisplayName);
    }

    /// <summary>A staged package becomes active when the panel composes it, and the instant is recorded.</summary>
    [Fact]
    public void A_staged_package_becomes_active_when_the_panel_composes_it()
    {
        var row = NewRow();
        var later = StagedAt.AddMinutes(5);

        row.MarkActive(later);

        Assert.Equal(InstalledPackageState.Active, row.State);
        Assert.Equal(later, row.StateChangedAt);
    }

    /// <summary>Marking an active package active again changes nothing, so a repeated reconcile is harmless.</summary>
    [Fact]
    public void Marking_an_active_package_active_again_changes_nothing()
    {
        var row = NewRow();
        row.MarkActive(StagedAt.AddMinutes(5));

        row.MarkActive(StagedAt.AddHours(1));

        Assert.Equal(StagedAt.AddMinutes(5), row.StateChangedAt);
    }

    /// <summary>A staged or an active package is superseded by a later one.</summary>
    /// <param name="activate">Whether the package was activated before it was superseded.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_staged_or_active_package_is_superseded_by_a_later_one(bool activate)
    {
        var row = NewRow();
        if (activate)
        {
            row.MarkActive(StagedAt.AddMinutes(1));
        }

        row.Supersede(StagedAt.AddMinutes(10));

        Assert.Equal(InstalledPackageState.Superseded, row.State);
    }

    /// <summary>A staged or an active package is closed by removal, which is recorded with its instant.</summary>
    /// <param name="activate">Whether the package was activated before it was removed.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_staged_or_active_package_is_closed_by_removal(bool activate)
    {
        var row = NewRow();
        if (activate)
        {
            row.MarkActive(StagedAt.AddMinutes(1));
        }

        row.Remove(StagedAt.AddMinutes(10));

        Assert.Equal(InstalledPackageState.Removed, row.State);
        Assert.Equal(StagedAt.AddMinutes(10), row.StateChangedAt);
    }

    /// <summary>A package that history has closed cannot be activated, superseded or removed again.</summary>
    [Fact]
    public void A_closed_package_cannot_change_state_again()
    {
        var superseded = NewRow();
        superseded.Supersede(StagedAt.AddMinutes(1));
        var removed = NewRow();
        removed.Remove(StagedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => { superseded.MarkActive(StagedAt.AddMinutes(2)); });
        Assert.Throws<InvalidOperationException>(() => { superseded.Remove(StagedAt.AddMinutes(2)); });
        Assert.Throws<InvalidOperationException>(() => { removed.Supersede(StagedAt.AddMinutes(2)); });
        Assert.Throws<InvalidOperationException>(() => { removed.MarkActive(StagedAt.AddMinutes(2)); });
    }
}
