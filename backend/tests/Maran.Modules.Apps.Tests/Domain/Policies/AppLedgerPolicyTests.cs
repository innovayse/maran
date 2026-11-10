using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.Policies;
using Maran.Modules.Apps.Domain.ValueObjects;

namespace Maran.Modules.Apps.Tests.Domain.Policies;

/// <summary>What the start-up pass does with a ledger row for each pairing of ledger state and host answer.</summary>
public sealed class AppLedgerPolicyTests
{
    /// <summary>The instant every row is created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a row in the given state.</summary>
    /// <param name="state">The state to put the row in.</param>
    /// <returns>The row.</returns>
    private static InstalledApp RowIn(AppState state)
    {
        Assert.True(AppId.TryParse("php-8.3", out var appId));
        var row = InstalledApp.BeginInstall(Guid.NewGuid(), appId, Guid.NewGuid(), Now);
        switch (state)
        {
            case AppState.Installed:
                row.MarkInstalled("8.3", Now);
                break;
            case AppState.Failed:
                row.MarkFailed(Now);
                break;
            case AppState.Removing:
                row.MarkInstalled("8.3", Now);
                row.BeginRemoval(Guid.NewGuid(), Now);
                break;
        }

        return row;
    }

    /// <summary>An installed row the host agrees with is left alone.</summary>
    [Fact]
    public void An_installed_row_at_the_hosts_version_is_kept()
    {
        Assert.Equal(AppLedgerVerdict.Keep, AppLedgerPolicy.Decide(RowIn(AppState.Installed), "8.3"));
    }

    /// <summary>The host wins on the version: a package updated from a shell refreshes the row.</summary>
    [Fact]
    public void An_installed_row_at_another_version_is_refreshed()
    {
        Assert.Equal(AppLedgerVerdict.MarkInstalled, AppLedgerPolicy.Decide(RowIn(AppState.Installed), "8.4"));
    }

    /// <summary>A process that died mid-install or mid-removal leaves a row the host has settled.</summary>
    /// <param name="state">The state the dead process left.</param>
    [Theory]
    [InlineData(AppState.Installing)]
    [InlineData(AppState.Failed)]
    [InlineData(AppState.Removing)]
    public void A_row_the_host_has_becomes_installed(AppState state)
    {
        Assert.Equal(AppLedgerVerdict.MarkInstalled, AppLedgerPolicy.Decide(RowIn(state), "8.3"));
    }

    /// <summary>An install that left nothing behind says it failed rather than vanishing.</summary>
    [Fact]
    public void An_install_the_host_does_not_have_is_marked_failed()
    {
        Assert.Equal(AppLedgerVerdict.MarkFailed, AppLedgerPolicy.Decide(RowIn(AppState.Installing), null));
    }

    /// <summary>A failure already recorded stays visible until the administrator acts.</summary>
    [Fact]
    public void A_failed_row_the_host_does_not_have_is_kept()
    {
        Assert.Equal(AppLedgerVerdict.Keep, AppLedgerPolicy.Decide(RowIn(AppState.Failed), null));
    }

    /// <summary>A finished removal, and an app removed from a shell, leave no row.</summary>
    /// <param name="state">The state of the row.</param>
    [Theory]
    [InlineData(AppState.Removing)]
    [InlineData(AppState.Installed)]
    public void A_row_for_an_app_that_is_gone_is_deleted(AppState state)
    {
        Assert.Equal(AppLedgerVerdict.Delete, AppLedgerPolicy.Decide(RowIn(state), null));
    }

    /// <summary>A host that reports no version is an app that is present, not absent.</summary>
    [Fact]
    public void An_empty_host_version_is_presence()
    {
        Assert.Equal(AppLedgerVerdict.MarkInstalled, AppLedgerPolicy.Decide(RowIn(AppState.Installing), string.Empty));
    }
}
