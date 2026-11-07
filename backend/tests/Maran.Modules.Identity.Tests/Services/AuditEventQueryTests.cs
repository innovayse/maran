using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// What <see cref="AuditEventQuery"/> keeps: each filter alone and together,
/// and the per-user and per-account views. The actor filter and the search are <c>ILIKE</c> matches only
/// PostgreSQL evaluates (the in-memory provider cannot), so they are held by <c>AuditListEndpointTests</c>.
/// </summary>
public sealed class AuditEventQueryTests : IDisposable
{
    /// <summary>The instant the seeded entries count from.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory journal the query reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The query under test.</summary>
    private readonly AuditEventQuery _query;

    /// <summary>Creates the query over the in-memory journal.</summary>
    public AuditEventQueryTests()
    {
        _query = new AuditEventQuery(_context);
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A filter that states nothing keeps every entry — the inverse control for the narrowing tests.</summary>
    [Fact]
    public async Task A_filter_that_states_nothing_keeps_every_entry()
    {
        await SeedAsync("a");
        await SeedAsync("b");

        Assert.Equal(["a", "b"], Subjects(_query.Matching(Filter())));
    }

    /// <summary>The action filter keeps only entries recording that action.</summary>
    [Fact]
    public async Task The_action_filter_keeps_only_that_action()
    {
        await SeedAsync("signed-in");
        await SeedAsync("refused", action: "LoginFailed");

        Assert.Equal(["refused"], Subjects(_query.Matching(Filter(action: FilterValue.Of("LoginFailed")))));
    }

    /// <summary>The outcome filter keeps one side and the other keeps the other.</summary>
    [Fact]
    public async Task The_outcome_filter_keeps_only_entries_that_ended_that_way()
    {
        await SeedAsync("worked");
        await SeedAsync("broke", succeeded: false);

        Assert.Equal(["broke"], Subjects(_query.Matching(Filter(outcome: AuditOutcome.Failed))));
        Assert.Equal(["worked"], Subjects(_query.Matching(Filter(outcome: AuditOutcome.Succeeded))));
    }

    /// <summary>The window is inclusive at both ends and leaves out what lies outside it.</summary>
    [Fact]
    public async Task The_window_is_inclusive_at_both_ends()
    {
        await SeedAsync("before", at: Now.AddDays(-1));
        await SeedAsync("start", at: Now);
        await SeedAsync("end", at: Now.AddDays(1));
        await SeedAsync("after", at: Now.AddDays(2));

        Assert.Equal(["start", "end"], Subjects(_query.Matching(Filter(from: Now, to: Now.AddDays(1)))));
    }

    /// <summary>The account filter keeps only entries stamped with that account.</summary>
    [Fact]
    public async Task The_account_filter_keeps_only_that_accounts_entries()
    {
        var account = Guid.NewGuid();
        await SeedAsync("of-account", accountId: account);
        await SeedAsync("of-another", accountId: Guid.NewGuid());
        await SeedAsync("of-none");

        Assert.Equal(["of-account"], Subjects(_query.Matching(Filter(accountId: account))));
    }

    /// <summary>Several conditions narrow together: an entry must satisfy all of them.</summary>
    [Fact]
    public async Task Several_conditions_narrow_together()
    {
        var account = Guid.NewGuid();
        await SeedAsync("all", accountId: account, action: "LoginFailed", succeeded: false);
        await SeedAsync("wrong-action", accountId: account, succeeded: false);
        await SeedAsync("wrong-account", action: "LoginFailed", succeeded: false);
        await SeedAsync("wrong-outcome", accountId: account, action: "LoginFailed");

        var matching = _query.Matching(Filter(
            action: FilterValue.Of("LoginFailed"),
            outcome: AuditOutcome.Failed,
            accountId: account));

        Assert.Equal(["all"], Subjects(matching));
    }

    /// <summary>A user's view holds the entries they did and those they did as the viewing operator, and no others.</summary>
    [Fact]
    public async Task A_users_view_holds_what_they_did_and_what_they_did_as_the_viewing_operator()
    {
        var user = Guid.NewGuid();
        await SeedAsync("as-actor", actorUserId: user);
        await SeedAsync("as-operator", actorUserId: Guid.NewGuid(), impersonator: new AuditImpersonator(user, "root-admin"));
        await SeedAsync("someone-else", actorUserId: Guid.NewGuid());
        await SeedAsync("viewed-by-other", actorUserId: Guid.NewGuid(), impersonator: new AuditImpersonator(Guid.NewGuid(), "other"));

        Assert.Equal(["as-actor", "as-operator"], Subjects(_query.InvolvingUser(user)));
    }

    /// <summary>An account's view holds the entries stamped with it, and nothing for an account with none.</summary>
    [Fact]
    public async Task An_accounts_view_holds_the_entries_stamped_with_it()
    {
        var account = Guid.NewGuid();
        await SeedAsync("first", accountId: account);
        await SeedAsync("second", accountId: account);
        await SeedAsync("other", accountId: Guid.NewGuid());

        Assert.Equal(["first", "second"], Subjects(_query.OfAccount(account)));
        Assert.Empty(_query.OfAccount(Guid.NewGuid()));
    }

    /// <summary>Builds a filter that states only what a test names.</summary>
    /// <param name="action">The action filter.</param>
    /// <param name="outcome">The outcome filter.</param>
    /// <param name="from">The window's start.</param>
    /// <param name="to">The window's end.</param>
    /// <param name="accountId">The account filter.</param>
    /// <returns>The filter.</returns>
    private static AuditEventFilter Filter(
        FilterValue action = default,
        AuditOutcome? outcome = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        Guid? accountId = null)
    {
        return new AuditEventFilter(action, outcome, from, to, SearchTerm.None, default, accountId);
    }

    /// <summary>Reads the subjects of a query's rows, oldest first, ties by subject, so a test's order is stated.</summary>
    /// <param name="events">The query to read.</param>
    /// <returns>The subjects.</returns>
    private static List<string> Subjects(IQueryable<AuditEvent> events)
    {
        return events
            .OrderBy(auditEvent => auditEvent.OccurredAt)
            .ThenBy(auditEvent => auditEvent.Subject)
            .Select(auditEvent => auditEvent.Subject)
            .ToList();
    }

    /// <summary>Stores one entry.</summary>
    /// <param name="subject">What it was done to; the tests identify rows by it.</param>
    /// <param name="at">When it happened; the default is the fixture's instant.</param>
    /// <param name="action">What was done.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <param name="actor">The name the actor used.</param>
    /// <param name="actorUserId">The actor's user id, or <c>null</c>.</param>
    /// <param name="impersonator">The operator viewing as the actor, or <c>null</c>.</param>
    /// <param name="accountId">The account the entry is stamped with, or <c>null</c>.</param>
    /// <returns>Resolves once the entry is saved.</returns>
    private async Task SeedAsync(
        string subject,
        DateTimeOffset? at = null,
        string action = "LoginSucceeded",
        bool succeeded = true,
        string actor = "admin",
        Guid? actorUserId = null,
        AuditImpersonator? impersonator = null,
        Guid? accountId = null)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            at ?? Now,
            actorUserId,
            actor,
            action,
            subject,
            "203.0.113.7",
            "agent",
            succeeded,
            null,
            impersonator,
            accountId));
        await _context.SaveChangesAsync();
    }
}
