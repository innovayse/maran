using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// What pseudonymising a deleted person or a deleted account changes in the audit journal, and, as
/// importantly, what it leaves exactly as it was.
/// </summary>
public sealed class AuditPseudonymiserTests : IDisposable
{
    /// <summary>The person being erased.</summary>
    private static readonly Guid Alice = Guid.Parse("a11ce000-0000-4000-8000-000000000001");

    /// <summary>A living person whose rows must survive an erasure of somebody else.</summary>
    private static readonly Guid Bob = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    /// <summary>The account being erased.</summary>
    private static readonly Guid AccountA = Guid.Parse("aaaaaaaa-0000-4000-8000-00000000000a");

    /// <summary>An account that is not being erased.</summary>
    private static readonly Guid AccountB = Guid.Parse("bbbbbbbb-0000-4000-8000-00000000000b");

    /// <summary>The address column of the erased person's rows, distinct from every other row's.</summary>
    private const string AliceAddress = "198.51.100.99";

    /// <summary>The user-agent column of the erased person's rows, distinct from every other row's.</summary>
    private const string AliceAgent = "alice-browser/3";

    /// <summary>The erased person's e-mail address.</summary>
    private const string AliceEmail = "alice@example.test";

    /// <summary>The database every test seeds and reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The journal double the pseudonymiser writes its summaries into.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Releases the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A row the person acted in loses their id, name, address and client, and keeps what happened and when.</summary>
    [Fact]
    public async Task A_row_the_person_acted_in_loses_their_id_name_address_and_client()
    {
        var seeded = ErasureJournal.Row(Alice, "alice", "deploy-site", AliceAddress, AliceAgent);
        _context.AuditEvents.Add(seeded);
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseUserAsync(Alice, [AliceEmail, "alice"], CancellationToken.None);

        var row = Assert.Single(await ErasureJournal.AllAsync(_context));
        Assert.Equal(seeded.Id, row.Id);
        Assert.Equal(seeded.OccurredAt, row.OccurredAt);
        Assert.Equal(seeded.Action, row.Action);
        Assert.True(row.Succeeded);
        Assert.Equal("deploy-site", row.Subject);
        Assert.NotEqual(Alice, row.ActorUserId);
        Assert.NotNull(row.ActorUserId);
        Assert.NotEqual(Guid.Empty, row.ActorUserId);
        Assert.Equal(outcome.Pseudonym, row.ActorUsername);
        Assert.StartsWith("erased-user-", outcome.Pseudonym);
        Assert.Equal(string.Empty, row.IpAddress);
        Assert.Equal(string.Empty, row.UserAgent);
        Assert.Equal(ErasureHandlers.Now, row.ErasedAt);
        Assert.Equal(1, outcome.Rows);
    }

    /// <summary>All of one person's rows share one pseudonym, so the history stays readable as one history.</summary>
    [Fact]
    public async Task All_of_one_persons_rows_share_one_pseudonym()
    {
        _context.AuditEvents.AddRange(
            ErasureJournal.Row(Alice, "alice", "one", AliceAddress, AliceAgent),
            ErasureJournal.Row(Alice, "alice", "two", AliceAddress, AliceAgent));
        await _context.SaveChangesAsync();

        await Pseudonymiser().PseudonymiseUserAsync(Alice, [], CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.Single(rows.Select(row => { return row.ActorUserId; }).Distinct());
        Assert.Single(rows.Select(row => { return row.ActorUsername; }).Distinct());
    }

    /// <summary>A row done to the person as a verified subject login loses the subject and keeps the living actor's columns.</summary>
    [Fact]
    public async Task A_row_about_the_person_as_subject_login_loses_the_subject_and_keeps_the_living_actor()
    {
        _context.AuditEvents.AddRange(
            ErasureJournal.Row(Bob, "bob", "alice", subjectUserId: Alice),
            ErasureJournal.Row(Bob, "bob", AliceEmail, subjectUserId: Alice));
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseUserAsync(Alice, [AliceEmail, "alice"], CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.Equal(2, outcome.Rows);
        Assert.All(rows, row =>
        {
            Assert.Equal(outcome.Pseudonym, row.Subject);
            Assert.NotEqual(Alice, row.SubjectUserId);
            Assert.Equal(Bob, row.ActorUserId);
            Assert.Equal("bob", row.ActorUsername);
            Assert.Equal("203.0.113.7", row.IpAddress);
            Assert.Equal("agent/1.0", row.UserAgent);
        });
    }

    /// <summary>
    /// The attack of issue #464: rows that only TYPE a name (a refused sign-in, any case) or name it as
    /// somebody else's subject text are untouched when a login with that name is erased; the inverse
    /// control, the same erasure, still erases the login's own rows.
    /// </summary>
    [Fact]
    public async Task Erasing_a_login_named_like_other_rows_text_leaves_those_rows_untouched_and_erases_its_own()
    {
        var sprayedFrom = ErasureJournal.Row(
            null, "Root", "Root", "198.51.100.5", "sprayer/1", action: IdentityAuditActions.LoginFailed);
        var textOnly = ErasureJournal.Row(Bob, "bob", "root");
        var idAsText = ErasureJournal.Row(null, Alice.ToString("D"), Alice.ToString("D"), "198.51.100.6", "sprayer/2");
        var own = ErasureJournal.Row(Alice, "root", "deploy-site", AliceAddress, AliceAgent);
        var rowsBefore = new[] { sprayedFrom, textOnly, idAsText }.Select(ErasureJournal.TextOf).ToList();
        _context.AuditEvents.AddRange(sprayedFrom, textOnly, idAsText, own);
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseUserAsync(Alice, ["root", AliceEmail], CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.Equal(1, outcome.Rows);
        foreach (var (id, before) in new[] { sprayedFrom, textOnly, idAsText }.Zip(rowsBefore))
        {
            var after = rows.Single(row => { return row.Id == id.Id; });
            Assert.Equal(before, ErasureJournal.TextOf(after));
            Assert.Null(after.ErasedAt);
        }

        var erased = rows.Single(row => { return row.Id == own.Id; });
        Assert.Equal(outcome.Pseudonym, erased.ActorUsername);
        Assert.Equal(string.Empty, erased.IpAddress);
        Assert.Equal(string.Empty, erased.UserAgent);
    }

    /// <summary>On a row the person acted in, a subject equal to their name, address or id is replaced.</summary>
    [Fact]
    public async Task A_subject_equal_to_the_persons_own_name_on_a_row_they_acted_in_is_replaced()
    {
        _context.AuditEvents.AddRange(
            ErasureJournal.Row(Alice, "alice", "ALICE"),
            ErasureJournal.Row(Alice, "alice", "Alice@Example.Test"),
            ErasureJournal.Row(Alice, "alice", Alice.ToString("D")),
            ErasureJournal.Row(Alice, "alice", "somebody-else"));
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseUserAsync(Alice, [AliceEmail, "alice"], CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.Equal(4, outcome.Rows);
        Assert.Equal(3, rows.Count(row => { return row.Subject == outcome.Pseudonym; }));
        Assert.Single(rows, row => { return row.Subject == "somebody-else"; });
    }

    /// <summary>A row where the person only WATCHED as an operator keeps the viewed person's actor columns and loses the operator.</summary>
    [Fact]
    public async Task A_row_where_the_person_was_the_viewing_operator_keeps_the_living_actor()
    {
        var carol = Guid.Parse("ca401000-0000-4000-8000-000000000003");
        _context.AuditEvents.Add(ErasureJournal.Row(
            carol, "carol", "site", AliceAddress, AliceAgent, new AuditImpersonator(Alice, "alice")));
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseUserAsync(Alice, [], CancellationToken.None);

        var row = Assert.Single(await ErasureJournal.AllAsync(_context));
        Assert.Equal(carol, row.ActorUserId);
        Assert.Equal("carol", row.ActorUsername);
        Assert.NotNull(row.Impersonator);
        Assert.NotEqual(Alice, row.Impersonator.OperatorId);
        Assert.Equal(outcome.Pseudonym, row.Impersonator.Username);
        Assert.Equal(string.Empty, row.IpAddress);
        Assert.Equal(string.Empty, row.UserAgent);
    }

    /// <summary>A row that names somebody else entirely is not touched: nothing about it changes, its erasure time included.</summary>
    [Fact]
    public async Task A_row_about_somebody_else_is_left_exactly_as_it_was()
    {
        var seeded = ErasureJournal.Row(Bob, "bob", "bobs-site");
        _context.AuditEvents.Add(seeded);
        await _context.SaveChangesAsync();
        var before = ErasureJournal.TextOf(seeded);

        var outcome = await Pseudonymiser().PseudonymiseUserAsync(Alice, [AliceEmail, "alice"], CancellationToken.None);

        var row = Assert.Single(await ErasureJournal.AllAsync(_context));
        Assert.Equal(0, outcome.Rows);
        Assert.Equal(before, ErasureJournal.TextOf(row));
        Assert.Null(row.ErasedAt);
    }

    /// <summary>
    /// After the erasure no original id, name, address or client is left in ANY column of ANY row; the same
    /// search first finds every one of them in the seeded rows, so it is known to be able to.
    /// </summary>
    [Fact]
    public async Task No_original_identifier_survives_in_any_column_and_the_search_is_known_to_find_them()
    {
        var needles = new[] { Alice.ToString("D"), "alice", AliceEmail, AliceAddress, AliceAgent };
        _context.AuditEvents.AddRange(
            ErasureJournal.Row(Alice, "alice", "deploy-site", AliceAddress, AliceAgent),
            ErasureJournal.Row(Bob, "bob", AliceEmail, subjectUserId: Alice),
            ErasureJournal.Row(Bob, "bob", Alice.ToString("D"), subjectUserId: Alice),
            ErasureJournal.Row(Bob, "bob", "site", AliceAddress, AliceAgent, new AuditImpersonator(Alice, "alice")));
        await _context.SaveChangesAsync();
        Assert.True(ErasureJournal.Mentions(await ErasureJournal.AllAsync(_context), needles));

        await Pseudonymiser().PseudonymiseUserAsync(Alice, [AliceEmail, "alice"], CancellationToken.None);

        Assert.False(ErasureJournal.Mentions(await ErasureJournal.AllAsync(_context), needles));
    }

    /// <summary>More rows than one batch holds are all erased: the loop covers the set, not the first batch.</summary>
    [Fact]
    public async Task More_rows_than_one_batch_are_all_erased()
    {
        var count = AuditPseudonymiser.BatchSize + 5;
        _context.AuditEvents.AddRange(Enumerable.Range(0, count).Select(index =>
        {
            return ErasureJournal.Row(Alice, "alice", "site-" + index, AliceAddress, AliceAgent);
        }));
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseUserAsync(Alice, [], CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.Equal(count, outcome.Rows);
        Assert.Equal(count, rows.Count);
        Assert.All(rows, row => { Assert.NotNull(row.ErasedAt); });
        Assert.DoesNotContain(rows, row => { return row.ActorUserId == Alice; });
    }

    /// <summary>The caller's own loaded entities stay tracked, and the journal rows do not stay in the tracker.</summary>
    [Fact]
    public async Task The_callers_loaded_entities_stay_tracked_and_the_journal_rows_do_not()
    {
        var login = new User(Guid.NewGuid(), "owner", "owner@example.test", "hash", UserRole.Customer, ErasureHandlers.Now);
        _context.Users.Add(login);
        _context.AuditEvents.Add(ErasureJournal.Row(Alice, "alice", "site", AliceAddress, AliceAgent));
        await _context.SaveChangesAsync();

        await Pseudonymiser().PseudonymiseUserAsync(Alice, [], CancellationToken.None);

        Assert.Equal(EntityState.Unchanged, _context.Entry(login).State);
        Assert.Empty(_context.ChangeTracker.Entries<AuditEvent>());
    }

    /// <summary>The account rule replaces the subject and the account id, and keeps the living actor's columns.</summary>
    [Fact]
    public async Task The_account_rule_replaces_the_subject_and_the_account_id_and_keeps_the_actor()
    {
        _context.AuditEvents.Add(ErasureJournal.Row(
            Bob, "bob", "acme", AliceAddress, AliceAgent, accountId: AccountA, action: "AccountSuspended"));
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseAccountAsync(AccountA, CancellationToken.None);

        var row = Assert.Single(await ErasureJournal.AllAsync(_context));
        Assert.Equal(1, outcome.Rows);
        Assert.StartsWith("erased-account-", outcome.Pseudonym);
        Assert.Equal(outcome.Pseudonym, row.Subject);
        Assert.NotEqual(AccountA, row.AccountId);
        Assert.NotNull(row.AccountId);
        Assert.Equal(Bob, row.ActorUserId);
        Assert.Equal("bob", row.ActorUsername);
        Assert.Equal(AliceAddress, row.IpAddress);
        Assert.Equal(AliceAgent, row.UserAgent);
        Assert.Equal(ErasureHandlers.Now, row.ErasedAt);
    }

    /// <summary>Every row of the account shares one new account id, and another account's rows are untouched.</summary>
    [Fact]
    public async Task The_accounts_rows_share_one_new_id_and_another_accounts_rows_are_untouched()
    {
        _context.AuditEvents.AddRange(
            ErasureJournal.Row(Bob, "bob", "one", accountId: AccountA),
            ErasureJournal.Row(Bob, "bob", "two", accountId: AccountA),
            ErasureJournal.Row(Bob, "bob", "other-account-row", accountId: AccountB));
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseAccountAsync(AccountA, CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        var erased = rows.Where(row => { return row.Subject == outcome.Pseudonym; }).ToList();
        var kept = Assert.Single(rows, row => { return row.Subject == "other-account-row"; });
        Assert.Equal(2, outcome.Rows);
        Assert.Equal(2, erased.Count);
        Assert.Single(erased.Select(row => { return row.AccountId; }).Distinct());
        Assert.Equal(AccountB, kept.AccountId);
        Assert.Null(kept.ErasedAt);
    }

    /// <summary>A row that carries both the person and the account is handled by both rules, in either order of arrival.</summary>
    [Fact]
    public async Task A_row_with_both_the_person_and_the_account_is_handled_by_both_rules()
    {
        _context.AuditEvents.Add(ErasureJournal.Row(
            Alice, "alice", "acme", AliceAddress, AliceAgent, accountId: AccountA));
        await _context.SaveChangesAsync();
        var pseudonymiser = Pseudonymiser();

        var person = await pseudonymiser.PseudonymiseUserAsync(Alice, [], CancellationToken.None);
        var account = await pseudonymiser.PseudonymiseAccountAsync(AccountA, CancellationToken.None);

        var row = Assert.Single(await ErasureJournal.AllAsync(_context));
        Assert.Equal(person.Pseudonym, row.ActorUsername);
        Assert.Equal(account.Pseudonym, row.Subject);
        Assert.NotEqual(AccountA, row.AccountId);
        Assert.False(ErasureJournal.Mentions([row], Alice.ToString("D"), AccountA.ToString("D"), AliceAddress, AliceAgent));
    }

    /// <summary>An account no row carries changes nothing and says so.</summary>
    [Fact]
    public async Task An_account_no_row_carries_changes_nothing()
    {
        _context.AuditEvents.Add(ErasureJournal.Row(Bob, "bob", "site"));
        await _context.SaveChangesAsync();

        var outcome = await Pseudonymiser().PseudonymiseAccountAsync(AccountA, CancellationToken.None);

        Assert.Equal(0, outcome.Rows);
        Assert.Null(Assert.Single(await ErasureJournal.AllAsync(_context)).ErasedAt);
    }

    /// <summary>Builds the pseudonymiser over this test's database.</summary>
    /// <returns>The pseudonymiser under test.</returns>
    private AuditPseudonymiser Pseudonymiser()
    {
        return ErasureHandlers.Pseudonymiser(_context, _audit);
    }
}
