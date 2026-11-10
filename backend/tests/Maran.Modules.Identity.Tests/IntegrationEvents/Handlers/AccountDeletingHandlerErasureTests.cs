using System.Text.RegularExpressions;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What deleting an account does to the audit journal: the deleted logins and the account are
/// pseudonymised, everyone else is left alone, and one summary per erasure is written after the removal.
/// </summary>
public sealed class AccountDeletingHandlerErasureTests : IDisposable
{
    /// <summary>The address of the owner's own rows, distinct from every other row's.</summary>
    private const string OwnerAddress = "198.51.100.50";

    /// <summary>The user agent of the owner's own rows, distinct from every other row's.</summary>
    private const string OwnerAgent = "owner-browser/1";

    /// <summary>The owner's e-mail address.</summary>
    private const string OwnerEmail = "owner@acme.test";

    /// <summary>The account being deleted.</summary>
    private static readonly Guid AccountA = Guid.Parse("aaaaaaaa-0000-4000-8000-00000000000a");

    /// <summary>An account that is not being deleted.</summary>
    private static readonly Guid AccountB = Guid.Parse("bbbbbbbb-0000-4000-8000-00000000000b");

    /// <summary>An account with no login at all.</summary>
    private static readonly Guid AccountC = Guid.Parse("cccccccc-0000-4000-8000-00000000000c");

    /// <summary>The database every test seeds and reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The journal double the handler writes its summaries into.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The login that owns the account being deleted, and goes with it.</summary>
    private readonly User _owner = new(
        Guid.NewGuid(), "acme-owner", OwnerEmail, "hash", UserRole.Customer, ErasureHandlers.Now);

    /// <summary>A login that is a member of both accounts, and so outlives the deletion.</summary>
    private readonly User _dual = new(
        Guid.NewGuid(), "dual-member", "dual@example.test", "hash", UserRole.Customer, ErasureHandlers.Now);

    /// <summary>The login of the other account.</summary>
    private readonly User _stranger = new(
        Guid.NewGuid(), "stranger", "stranger@example.test", "hash", UserRole.Customer, ErasureHandlers.Now);

    /// <summary>The administrator who acts on the accounts.</summary>
    private readonly User _admin = new(
        Guid.NewGuid(), "administrator", "admin@example.test", "hash", UserRole.Admin, ErasureHandlers.Now);

    /// <summary>Releases the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// The deleted login's rows and the account's rows no longer name them; the same search first finds every
    /// original identifier in the seeded rows, so it is known to be able to.
    /// </summary>
    [Fact]
    public async Task Deleting_an_account_leaves_no_original_identifier_in_the_journal()
    {
        await SeedAsync(_context);
        var needles = Originals();
        Assert.True(ErasureJournal.Mentions(await ErasureJournal.AllAsync(_context), needles));

        await ErasureHandlers.AccountDeleting(_context, _audit).HandleAsync(Deleting(AccountA), CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.False(ErasureJournal.Mentions(rows, needles));
        Assert.Equal(6, rows.Count);
    }

    /// <summary>The accepting control: the administrator, the surviving member and the other account keep every column.</summary>
    [Fact]
    public async Task Deleting_an_account_keeps_the_administrator_the_surviving_member_and_the_other_account()
    {
        await SeedAsync(_context);
        var strangerBefore = ErasureJournal.TextOf(Assert.Single(
            await ErasureJournal.AllAsync(_context), row => { return row.ActorUserId == _stranger.Id; }));

        await ErasureHandlers.AccountDeleting(_context, _audit).HandleAsync(Deleting(AccountA), CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        var stranger = Assert.Single(rows, row => { return row.ActorUserId == _stranger.Id; });
        Assert.Equal(strangerBefore, ErasureJournal.TextOf(stranger));
        Assert.Null(stranger.ErasedAt);
        Assert.All(rows.Where(row => { return row.ActorUserId == _admin.Id; }), row =>
        {
            Assert.Equal("administrator", row.ActorUsername);
            Assert.Equal("203.0.113.7", row.IpAddress);
        });
        var member = Assert.Single(rows, row => { return row.ActorUserId == _dual.Id; });
        Assert.Equal("dual-member", member.ActorUsername);
        Assert.Equal("203.0.113.7", member.IpAddress);
    }

    /// <summary>One summary is written per erasure, holding the pseudonym and a count and nothing that names the person.</summary>
    [Fact]
    public async Task Each_erasure_is_summarised_by_a_pseudonym_and_a_count()
    {
        await SeedAsync(_context);

        await ErasureHandlers.AccountDeleting(_context, _audit).HandleAsync(Deleting(AccountA), CancellationToken.None);

        Assert.Equal(2, _audit.Written.Count);
        Assert.All(_audit.Written, entry =>
        {
            Assert.Equal(IdentityAuditActions.PersonalDataErased, entry.Action);
            Assert.True(entry.Succeeded);
            Assert.Equal("maran-identity", entry.ActorUsername);
            Assert.Null(entry.ActorUserId);
            Assert.DoesNotContain(Originals(), needle => { return entry.Subject.Contains(needle, StringComparison.OrdinalIgnoreCase); });
        });
        Assert.Matches(new Regex("^erased-user-[0-9a-f]{8} rows=2$"), _audit.Written[0].Subject);
        Assert.Matches(new Regex("^erased-account-[0-9a-f]{8} rows=3$"), _audit.Written[1].Subject);
    }

    /// <summary>A login that still holds another account is a person who still exists, so its own rows keep their subjects.</summary>
    [Fact]
    public async Task A_login_kept_because_it_holds_another_account_is_not_erased()
    {
        await SeedAsync(_context);
        _context.AuditEvents.Add(ErasureJournal.Row(_dual.Id, "dual-member", "dual-member"));
        await _context.SaveChangesAsync();

        await ErasureHandlers.AccountDeleting(_context, _audit).HandleAsync(Deleting(AccountA), CancellationToken.None);

        var own = Assert.Single(
            await ErasureJournal.AllAsync(_context), row => { return row.Subject == "dual-member"; });
        Assert.Equal(_dual.Id, own.ActorUserId);
        Assert.Null(own.ErasedAt);
    }

    /// <summary>An account no login ever reached still has its journal rows pseudonymised.</summary>
    [Fact]
    public async Task An_account_with_no_login_still_has_its_rows_pseudonymised()
    {
        _context.AuditEvents.Add(ErasureJournal.Row(_admin.Id, "administrator", "ghost-account", accountId: AccountC));
        await _context.SaveChangesAsync();

        await ErasureHandlers.AccountDeleting(_context, _audit).HandleAsync(Deleting(AccountC), CancellationToken.None);

        var row = Assert.Single(await ErasureJournal.AllAsync(_context));
        Assert.StartsWith("erased-account-", row.Subject);
        Assert.NotEqual(AccountC, row.AccountId);
        var summary = Assert.Single(_audit.Written);
        Assert.Matches(new Regex("^erased-account-[0-9a-f]{8} rows=1$"), summary.Subject);
    }

    /// <summary>A deletion that matched no row writes no summary, because there is nothing to account for.</summary>
    [Fact]
    public async Task A_deletion_that_matched_nothing_writes_no_summary()
    {
        await ErasureHandlers.AccountDeleting(_context, _audit).HandleAsync(Deleting(AccountC), CancellationToken.None);

        Assert.Empty(_audit.Written);
    }

    /// <summary>
    /// A deletion the database refuses writes no summary: the summary follows the commit, so it can never
    /// describe an erasure that did not complete.
    /// </summary>
    /// <remarks>
    /// UNOBSERVED HERE: whether the pseudonymised rows are rolled back. The in-memory provider keeps no
    /// transaction, so this asserts the ORDER (the summary is written only after the removal succeeded) and
    /// not the rollback itself.
    /// </remarks>
    [Fact]
    public async Task A_deletion_the_database_refuses_writes_no_summary()
    {
        using var refusing = RefusingIdentityTestContext.Create();
        await SeedAsync(refusing);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await ErasureHandlers.AccountDeleting(refusing, _audit).HandleAsync(Deleting(AccountA), CancellationToken.None);
        });

        Assert.Empty(_audit.Written);
    }

    /// <summary>Delivering the same deletion twice summarises nothing the second time: there is nothing left to erase.</summary>
    [Fact]
    public async Task Delivering_the_same_deletion_twice_writes_no_second_summary()
    {
        await SeedAsync(_context);
        var handler = ErasureHandlers.AccountDeleting(_context, _audit);
        await handler.HandleAsync(Deleting(AccountA), CancellationToken.None);
        var summaries = _audit.Written.Count;

        await handler.HandleAsync(Deleting(AccountA), CancellationToken.None);

        Assert.Equal(summaries, _audit.Written.Count);
    }

    /// <summary>Every original identifier the deleted logins and the account had in the seeded journal.</summary>
    /// <returns>The owner's id, name, e-mail, address and client, and the account's id.</returns>
    private string[] Originals()
    {
        return [_owner.Id.ToString("D"), "acme-owner", OwnerEmail, OwnerAddress, OwnerAgent, AccountA.ToString("D")];
    }

    /// <summary>Builds the message the Accounts module publishes.</summary>
    /// <param name="accountId">The account being deleted.</param>
    /// <returns>The announcement.</returns>
    private static AccountDeleting Deleting(Guid accountId)
    {
        return new AccountDeleting(accountId, "acme");
    }

    /// <summary>
    /// Stores the logins, their memberships and six journal rows: the owner's own, one about the owner's
    /// address by the administrator, two of the account, the surviving member's, and the other account's.
    /// </summary>
    /// <param name="context">The context to seed, which the caller disposes.</param>
    /// <returns>Resolves once everything is stored.</returns>
    private async Task SeedAsync(IdentityDbContext context)
    {
        context.Users.AddRange(_owner, _dual, _stranger, _admin);
        context.AccountMemberships.AddRange(
            AccountMembership.Own(Guid.NewGuid(), _owner.Id, AccountA, ErasureHandlers.Now),
            AccountMembership.Admit(Guid.NewGuid(), _dual.Id, AccountA, AccountFeature.None, ErasureHandlers.Now),
            AccountMembership.Own(Guid.NewGuid(), _dual.Id, AccountB, ErasureHandlers.Now),
            AccountMembership.Own(Guid.NewGuid(), _stranger.Id, AccountB, ErasureHandlers.Now));
        context.AuditEvents.AddRange(
            ErasureJournal.Row(_owner.Id, "acme-owner", "acme-owner", OwnerAddress, OwnerAgent, accountId: AccountA),
            ErasureJournal.Row(_admin.Id, "administrator", OwnerEmail, subjectUserId: _owner.Id),
            ErasureJournal.Row(_admin.Id, "administrator", "acme", accountId: AccountA, action: "AccountSuspended"),
            ErasureJournal.Row(_dual.Id, "dual-member", "site", accountId: AccountA),
            ErasureJournal.Row(_stranger.Id, "stranger", "stranger-site", accountId: AccountB),
            ErasureJournal.Row(_admin.Id, "administrator", "other-account", accountId: AccountB));
        await context.SaveChangesAsync();
    }
}
