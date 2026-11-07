using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// The orphaned-actor sweep: it erases the rows of a person who no longer exists, refuses when its own
/// input looks wrong, and leaves everyone else alone. Every refusal is paired with a case it must accept.
/// </summary>
public sealed class AuditPseudonymiserOrphanSweepTests : IDisposable
{
    /// <summary>A person whose login was deleted before erasure existed.</summary>
    private static readonly Guid Dead = Guid.Parse("dead0000-0000-4000-8000-000000000001");

    /// <summary>The address and client of the dead person's rows, distinct from every other row's.</summary>
    private const string DeadAddress = "198.51.100.66";

    /// <summary>The database every test seeds and reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The journal double the sweep writes its summary into.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>A login that still exists.</summary>
    private readonly User _live = new(
        Guid.NewGuid(), "live", "live@example.test", "hash", UserRole.Customer, ErasureHandlers.Now);

    /// <summary>Releases the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>The positive control: the rows of a person with no login are erased, and the summary says how many.</summary>
    [Fact]
    public async Task The_rows_of_a_person_with_no_login_are_erased()
    {
        await SeedWorldAsync();

        var erased = await Sweep().PseudonymiseOrphanedActorsAsync(CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.Equal(2, erased);
        Assert.DoesNotContain(rows, row => { return row.ActorUserId == Dead; });
        Assert.False(ErasureJournal.Mentions(rows, Dead.ToString("D"), "dormant", DeadAddress));
        var summary = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.PersonalDataErased, summary.Action);
        Assert.True(summary.Succeeded);
        Assert.Equal("maran-identity", summary.ActorUsername);
        Assert.Equal("orphaned-actors actors=1 rows=2", summary.Subject);
    }

    /// <summary>The inverse control: a live login's rows, the empty id and a missing actor are never touched.</summary>
    [Fact]
    public async Task A_live_logins_rows_the_empty_id_and_a_missing_actor_are_left_alone()
    {
        await SeedWorldAsync();

        await Sweep().PseudonymiseOrphanedActorsAsync(CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        var live = Assert.Single(rows, row => { return row.ActorUserId == _live.Id; });
        var empty = Assert.Single(rows, row => { return row.ActorUserId == Guid.Empty; });
        var missing = Assert.Single(rows, row => { return row.ActorUserId is null; });
        Assert.Equal("live", live.ActorUsername);
        Assert.Equal("203.0.113.7", live.IpAddress);
        Assert.All([live, empty, missing], row => { Assert.Null(row.ErasedAt); });
    }

    /// <summary>The sweep gives the same answer for the caller the maintenance job really has: nobody.</summary>
    [Fact]
    public async Task The_sweep_works_the_same_for_an_anonymous_caller()
    {
        await SeedWorldAsync();

        var erased = await Sweep(new AnonymousUser()).PseudonymiseOrphanedActorsAsync(CancellationToken.None);

        Assert.Equal(2, erased);
        Assert.DoesNotContain(await ErasureJournal.AllAsync(_context), row => { return row.ActorUserId == Dead; });
        Assert.Single(_audit.Written);
    }

    /// <summary>A second run finds nothing: the pseudonymised rows are not mistaken for orphans again.</summary>
    [Fact]
    public async Task A_second_run_erases_nothing_more()
    {
        await SeedWorldAsync();
        await Sweep().PseudonymiseOrphanedActorsAsync(CancellationToken.None);
        var after = (await ErasureJournal.AllAsync(_context)).Select(ErasureJournal.TextOf).Order().ToList();

        var erased = await Sweep().PseudonymiseOrphanedActorsAsync(CancellationToken.None);

        Assert.Equal(0, erased);
        Assert.Equal(after, (await ErasureJournal.AllAsync(_context)).Select(ErasureJournal.TextOf).Order().ToList());
        Assert.Single(_audit.Written);
    }

    /// <summary>
    /// A row an account's erasure already stamped is still erased as the dead person's: the sweep goes by
    /// the pseudonym name and not by the erasure time.
    /// </summary>
    [Fact]
    public async Task A_row_an_account_erasure_stamped_is_still_erased_as_the_dead_persons()
    {
        var account = Guid.NewGuid();
        _context.Users.Add(_live);
        _context.AuditEvents.Add(ErasureJournal.Row(Dead, "dormant", "acme", DeadAddress, DeadAddress, accountId: account));
        await _context.SaveChangesAsync();
        await Sweep().PseudonymiseAccountAsync(account, CancellationToken.None);
        Assert.NotNull(Assert.Single(await ErasureJournal.AllAsync(_context)).ErasedAt);

        var erased = await Sweep().PseudonymiseOrphanedActorsAsync(CancellationToken.None);

        Assert.Equal(1, erased);
        Assert.DoesNotContain(await ErasureJournal.AllAsync(_context), row => { return row.ActorUserId == Dead; });
    }

    /// <summary>With no login in the table every actor would read as deleted, so the sweep erases nothing and says why.</summary>
    [Fact]
    public async Task The_sweep_refuses_when_the_login_table_is_empty()
    {
        _context.AuditEvents.Add(ErasureJournal.Row(Dead, "dormant", "site", DeadAddress, DeadAddress));
        await _context.SaveChangesAsync();

        var erased = await Sweep().PseudonymiseOrphanedActorsAsync(CancellationToken.None);

        var row = Assert.Single(await ErasureJournal.AllAsync(_context));
        Assert.Equal(0, erased);
        Assert.Equal(Dead, row.ActorUserId);
        Assert.Null(row.ErasedAt);
        var refusal = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.PersonalDataErased, refusal.Action);
        Assert.False(refusal.Succeeded);
        Assert.Contains("empty", refusal.Subject, StringComparison.Ordinal);
    }

    /// <summary>More orphaned actors than the cap means something is wrong with the input: nothing is erased, and the count is journalled.</summary>
    [Fact]
    public async Task The_sweep_refuses_above_the_cap_and_names_the_count()
    {
        await SeedOrphanActorsAsync(AuditPseudonymiser.MaxOrphanActorsPerRun + 1);

        var erased = await Sweep().PseudonymiseOrphanedActorsAsync(CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.Equal(0, erased);
        Assert.All(rows, row => { Assert.Null(row.ErasedAt); });
        var refusal = Assert.Single(_audit.Written);
        Assert.False(refusal.Succeeded);
        Assert.Contains((AuditPseudonymiser.MaxOrphanActorsPerRun + 1).ToString(CultureInfo.InvariantCulture), refusal.Subject, StringComparison.Ordinal);
    }

    /// <summary>The inverse control of the cap: exactly the cap is erased.</summary>
    [Fact]
    public async Task The_sweep_erases_exactly_the_cap()
    {
        await SeedOrphanActorsAsync(AuditPseudonymiser.MaxOrphanActorsPerRun);

        var erased = await Sweep().PseudonymiseOrphanedActorsAsync(CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.Equal(AuditPseudonymiser.MaxOrphanActorsPerRun, erased);
        Assert.All(rows, row => { Assert.NotNull(row.ErasedAt); });
        Assert.True(Assert.Single(_audit.Written).Succeeded);
    }

    /// <summary>Stores one live login, two rows of the dead person, and the rows the sweep must never touch.</summary>
    /// <returns>Resolves once the journal is stored.</returns>
    private async Task SeedWorldAsync()
    {
        _context.Users.Add(_live);
        _context.AuditEvents.AddRange(
            ErasureJournal.Row(_live.Id, "live", "own-site"),
            ErasureJournal.Row(Dead, "dormant", "one", DeadAddress, DeadAddress),
            ErasureJournal.Row(Dead, "dormant", "two", DeadAddress, DeadAddress),
            ErasureJournal.Row(Guid.Empty, "maran-system", "legacy"),
            ErasureJournal.Row(null, "claimed-name", "failed-login"));
        await _context.SaveChangesAsync();
    }

    /// <summary>Stores one live login and one row for each of <paramref name="count"/> different dead people.</summary>
    /// <param name="count">How many distinct orphaned actors to create.</param>
    /// <returns>Resolves once the journal is stored.</returns>
    private async Task SeedOrphanActorsAsync(int count)
    {
        _context.Users.Add(_live);
        _context.AuditEvents.AddRange(Enumerable.Range(0, count).Select(index =>
        {
            return ErasureJournal.Row(Guid.NewGuid(), "dead-" + index, "site-" + index);
        }));
        await _context.SaveChangesAsync();
    }

    /// <summary>Builds the pseudonymiser over this test's database.</summary>
    /// <param name="caller">Who the journal reads as the current principal; an administrator when null.</param>
    /// <returns>The pseudonymiser under test.</returns>
    private AuditPseudonymiser Sweep(ICurrentUser? caller = null)
    {
        return ErasureHandlers.Pseudonymiser(_context, _audit, caller);
    }

    /// <summary>A principal that is nobody: the caller a background job runs as.</summary>
    private sealed class AnonymousUser : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller
        {
            get { return AnonymousCaller.Instance; }
        }
    }
}
