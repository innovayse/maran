using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Tests.TestSupport;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>The ledger records what the panel staged, keeps one open row per module, and its floor never goes down.</summary>
public sealed class InstalledPackageLedgerTests
{
    /// <summary>A package digest.</summary>
    private static readonly string Digest = new('b', 64);

    /// <summary>The administrator who asks.</summary>
    private static readonly Guid Administrator = Guid.NewGuid();

    /// <summary>The clock the ledger reads.</summary>
    private readonly AdvancingClock _clock = new();

    /// <summary>The ledger over a database of its own.</summary>
    private readonly InstalledPackageLedger _ledger;

    /// <summary>Builds the ledger over an isolated in-memory database.</summary>
    public InstalledPackageLedgerTests()
    {
        _ledger = new InstalledPackageLedger(MarketplaceDatabase.CreateIsolated(), _clock);
    }

    /// <summary>The first package of a module is staged with no previous version and starts the floor.</summary>
    [Fact]
    public async Task The_first_package_of_a_module_is_staged_with_no_previous_version_and_starts_the_floor()
    {
        var staged = await _ledger.RecordStagedAsync("mail", "1.0.0", Digest, Administrator, CancellationToken.None);

        Assert.Equal(InstalledPackageState.Staged, staged.State);
        Assert.Null(staged.PreviousVersion);
        Assert.Equal(Administrator, staged.InstalledBy);
        Assert.Equal("1.0.0", (await _ledger.ReadFloorAsync("mail", CancellationToken.None))?.ToString());
    }

    /// <summary>The catalogue's name is kept with the row, shortened to its column, and a blank name keeps nothing.</summary>
    [Fact]
    public async Task The_catalogue_name_is_kept_with_the_row_and_shortened_to_its_column()
    {
        var named = await _ledger.RecordStagedAsync(
            "mail", "1.0.0", Digest, Administrator, CancellationToken.None, "  Mail server  ");
        var blank = await _ledger.RecordStagedAsync(
            "dns", "1.0.0", Digest, Administrator, CancellationToken.None, "   ");
        var oversized = await _ledger.RecordStagedAsync(
            "sites", "1.0.0", Digest, Administrator, CancellationToken.None, new string('x', 400));

        Assert.Equal("Mail server", named.DisplayName);
        Assert.Null(blank.DisplayName);
        Assert.Equal(128, oversized.DisplayName!.Length);
    }

    /// <summary>The newest kept name is found whatever became of its row; a module that never kept one has none.</summary>
    [Fact]
    public async Task The_newest_kept_name_survives_removal_and_an_unnamed_later_row()
    {
        await _ledger.RecordStagedAsync("mail", "1.0.0", Digest, Administrator, CancellationToken.None, "Mail");
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _ledger.RecordStagedAsync("mail", "1.1.0", Digest, Administrator, CancellationToken.None, "Mail server");
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _ledger.RecordStagedAsync("mail", "1.2.0", Digest, Administrator, CancellationToken.None);
        await _ledger.RecordRemovalAsync("mail", CancellationToken.None);
        await _ledger.RecordStagedAsync("dns", "1.0.0", Digest, Administrator, CancellationToken.None);

        Assert.Equal("Mail server", await _ledger.FindDisplayNameAsync("mail", CancellationToken.None));
        Assert.Null(await _ledger.FindDisplayNameAsync("dns", CancellationToken.None));
        Assert.Null(await _ledger.FindDisplayNameAsync("unknown", CancellationToken.None));
    }

    /// <summary>An update supersedes the open row, names it as the previous version, and raises the floor.</summary>
    [Fact]
    public async Task An_update_supersedes_the_open_row_names_it_as_previous_and_raises_the_floor()
    {
        await _ledger.RecordStagedAsync("mail", "1.0.0", Digest, Administrator, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var updated = await _ledger.RecordStagedAsync("mail", "1.1.0", Digest, Administrator, CancellationToken.None);

        Assert.Equal("1.0.0", updated.PreviousVersion);
        var history = await _ledger.ListHistoryAsync("mail", CancellationToken.None);
        Assert.Equal(["1.1.0", "1.0.0"], history.Select(row => { return row.Version; }));
        Assert.Equal(InstalledPackageState.Superseded, history[1].State);
        Assert.Equal("1.1.0", (await _ledger.ReadFloorAsync("mail", CancellationToken.None))?.ToString());
    }

    /// <summary>A rollback stages the older package but a lower version never lowers the floor.</summary>
    [Fact]
    public async Task A_rollback_stages_the_older_package_but_the_floor_stays_at_the_highest_version()
    {
        await _ledger.RecordStagedAsync("mail", "1.0.0", Digest, Administrator, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _ledger.RecordStagedAsync("mail", "1.1.0", Digest, Administrator, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var rolledBack = await _ledger.RecordStagedAsync("mail", "1.0.0", Digest, Administrator, CancellationToken.None);

        Assert.Equal("1.1.0", rolledBack.PreviousVersion);
        var current = await _ledger.FindCurrentAsync("mail", CancellationToken.None);
        Assert.Equal("1.0.0", current?.Version);
        Assert.Equal("1.1.0", (await _ledger.ReadFloorAsync("mail", CancellationToken.None))?.ToString());
    }

    /// <summary>A module that was never staged has no floor.</summary>
    [Fact]
    public async Task A_module_that_was_never_staged_has_no_floor()
    {
        Assert.Null(await _ledger.ReadFloorAsync("mail", CancellationToken.None));
        Assert.Null(await _ledger.FindCurrentAsync("mail", CancellationToken.None));
    }

    /// <summary>Activation marks the staged row of that version active, and is refused for a version never staged.</summary>
    [Fact]
    public async Task Activation_marks_the_staged_row_active_and_ignores_a_version_never_staged()
    {
        await _ledger.RecordStagedAsync("mail", "1.0.0", Digest, Administrator, CancellationToken.None);

        Assert.False(await _ledger.RecordActivatedAsync("mail", "9.9.9", CancellationToken.None));
        Assert.True(await _ledger.RecordActivatedAsync("mail", "1.0.0", CancellationToken.None));

        Assert.Equal(InstalledPackageState.Active, (await _ledger.FindCurrentAsync("mail", CancellationToken.None))?.State);
        Assert.False(await _ledger.RecordActivatedAsync("mail", "1.0.0", CancellationToken.None));
    }

    /// <summary>Removal closes the open row, keeps the floor, and finds nothing to remove the second time.</summary>
    [Fact]
    public async Task Removal_closes_the_open_row_keeps_the_floor_and_finds_nothing_the_second_time()
    {
        await _ledger.RecordStagedAsync("mail", "1.2.0", Digest, Administrator, CancellationToken.None);

        Assert.True(await _ledger.RecordRemovalAsync("mail", CancellationToken.None));
        Assert.False(await _ledger.RecordRemovalAsync("mail", CancellationToken.None));

        Assert.Null(await _ledger.FindCurrentAsync("mail", CancellationToken.None));
        Assert.Equal("1.2.0", (await _ledger.ReadFloorAsync("mail", CancellationToken.None))?.ToString());
        var history = await _ledger.ListHistoryAsync("mail", CancellationToken.None);
        Assert.Equal(InstalledPackageState.Removed, Assert.Single(history).State);
    }

    /// <summary>The history of one module excludes another's, and with no module named it lists both.</summary>
    [Fact]
    public async Task The_history_of_one_module_excludes_another_and_no_module_lists_both()
    {
        await _ledger.RecordStagedAsync("mail", "1.0.0", Digest, Administrator, CancellationToken.None);
        await _ledger.RecordStagedAsync("dns", "2.0.0", Digest, Administrator, CancellationToken.None);

        Assert.Equal("mail", Assert.Single(await _ledger.ListHistoryAsync("mail", CancellationToken.None)).ModuleId);
        Assert.Equal(2, (await _ledger.ListHistoryAsync(null, CancellationToken.None)).Count);
    }

    /// <summary>Floors are per module: a high version of one module does not constrain another.</summary>
    [Fact]
    public async Task Floors_are_per_module()
    {
        await _ledger.RecordStagedAsync("mail", "5.0.0", Digest, Administrator, CancellationToken.None);
        await _ledger.RecordStagedAsync("dns", "1.0.0", Digest, Administrator, CancellationToken.None);

        Assert.Equal("1.0.0", (await _ledger.ReadFloorAsync("dns", CancellationToken.None))?.ToString());
    }
}
