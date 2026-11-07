using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ExportPersonalData;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Identity.Tests.Queries.ExportPersonalData;

/// <summary>Behavioural contract of the personal-data export planner (issue #146).</summary>
public sealed class ExportPersonalDataQueryHandlerTests : IDisposable
{
    /// <summary>The instant the fake clock starts at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The account every test is about.</summary>
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>The user every test is about.</summary>
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>The module's context over its own in-memory database.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Keeps the entries the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The administrator asking.</summary>
    private readonly StubCurrentUser _administrator = new(Guid.Parse("99999999-9999-9999-9999-999999999999"), "admin");

    /// <summary>Knows the one account the tests ask about.</summary>
    private readonly StubAccountDirectory _accounts = new();

    /// <summary>The provider standing in for every other module's.</summary>
    private readonly FixedPersonalDataProvider _provider = new(new PersonalDataSection(
        "sites",
        "sites",
        [new PersonalDataRecord(new Dictionary<string, string?> { ["domain"] = "acme.example.com" })]));

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds the handler over this test's doubles.</summary>
    private ExportPersonalDataQueryHandler NewHandler()
    {
        return new ExportPersonalDataQueryHandler(
            _context,
            _accounts,
            [_provider],
            new PersonalDataAuditRows(new AuditEventQuery(_context)),
            new IdentityAuditJournal(_audit, _administrator),
            _administrator,
            new FakeClock(Now));
    }

    /// <summary>Builds a query the way the controller does.</summary>
    /// <param name="kind">The subject's kind.</param>
    /// <param name="id">The subject's id.</param>
    private static ExportPersonalDataQuery Query(PersonalDataSubjectKind kind, Guid id)
    {
        return new ExportPersonalDataQuery(kind, id, "203.0.113.7", "agent");
    }

    /// <summary>Stores one journal row.</summary>
    /// <param name="actor">The verified actor, or null.</param>
    /// <param name="accountId">The account the row is stamped with, or null.</param>
    /// <param name="impersonator">The operator viewing as the actor, or null.</param>
    private void AddEntry(Guid? actor, Guid? accountId = null, AuditImpersonator? impersonator = null)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), Now, actor, "someone", "SiteCreated", "x.example.com", "192.0.2.1", "ua", true, null,
            impersonator, accountId));
    }

    /// <summary>An id no user and no account holds is a 404 with the privacy code, and nothing is journalled.</summary>
    [Fact]
    public async Task An_unknown_subject_is_not_found_and_leaves_no_journal_entry()
    {
        var user = await NewHandler().HandleAsync(Query(PersonalDataSubjectKind.User, UserId), CancellationToken.None);
        var account = await NewHandler().HandleAsync(Query(PersonalDataSubjectKind.Account, AccountId), CancellationToken.None);

        Assert.Equal("PrivacySubjectNotFound", user.Error!.Code);
        Assert.Equal(ErrorType.NotFound, user.Error.Type);
        Assert.Equal("PrivacySubjectNotFound", account.Error!.Code);
        Assert.Empty(_audit.Written);
        Assert.Empty(_provider.Asked);
    }

    /// <summary>A kind that is neither a user nor an account is refused as an unknown subject.</summary>
    [Fact]
    public async Task A_kind_that_is_neither_user_nor_account_is_not_found()
    {
        var result = await NewHandler().HandleAsync(Query(default, UserId), CancellationToken.None);

        Assert.Equal("PrivacySubjectNotFound", result.Error!.Code);
    }

    /// <summary>A known account yields a plan with the providers' sections, the bound row count and a clock-built file name.</summary>
    [Fact]
    public async Task A_known_account_yields_the_sections_the_bound_and_a_clock_built_file_name()
    {
        _accounts.Add(AccountId, "acme", maxFtpUsers: 1);
        AddEntry(null, AccountId);
        AddEntry(null, AccountId);
        AddEntry(null, Guid.NewGuid());
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Query(PersonalDataSubjectKind.Account, AccountId), CancellationToken.None);

        var plan = result.Value;
        Assert.Equal(new PersonalDataSubject(PersonalDataSubjectKind.Account, AccountId), plan.Subject);
        Assert.Equal(2, plan.AuditRowCount);
        Assert.Equal("sites", Assert.Single(plan.Sections).Module);
        Assert.Equal("maran-personal-data-account-20261107T093000Z.json", plan.FileName);
        Assert.Equal(plan.Subject, Assert.Single(_provider.Asked));
    }

    /// <summary>A user's journal rows are the ones they acted in and the ones an operator wrote while viewing as them.</summary>
    [Fact]
    public async Task A_users_audit_rows_are_those_they_acted_in_and_those_an_operator_made_viewing_as_them()
    {
        _context.Users.Add(new User(UserId, "olive", "olive@example.com", "hash", UserRole.Customer, Now));
        AddEntry(UserId);
        AddEntry(Guid.NewGuid(), impersonator: new AuditImpersonator(UserId, "olive"));
        AddEntry(Guid.NewGuid());
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Query(PersonalDataSubjectKind.User, UserId), CancellationToken.None);

        Assert.Equal(2, result.Value.AuditRowCount);
        Assert.Equal("maran-personal-data-user-20261107T093000Z.json", result.Value.FileName);
    }

    /// <summary>A successful plan is journalled with the kind and the id, as the administrator, from the stamped address.</summary>
    [Fact]
    public async Task A_planned_export_is_journalled_with_the_kind_the_id_and_the_stamped_caller()
    {
        _context.Users.Add(new User(UserId, "olive", "olive@example.com", "hash", UserRole.Customer, Now));
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Query(PersonalDataSubjectKind.User, UserId), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.PersonalDataExported, entry.Action);
        Assert.Equal(_administrator.UserId, entry.ActorUserId);
        Assert.Equal($"User {UserId:D}", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("agent", entry.UserAgent);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The journal entry names neither the person's e-mail address nor their username.</summary>
    [Fact]
    public async Task The_journal_entry_carries_no_identifier_of_the_person_beyond_the_id()
    {
        _context.Users.Add(new User(UserId, "olive", "olive@example.com", "hash", UserRole.Customer, Now));
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Query(PersonalDataSubjectKind.User, UserId), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.DoesNotContain("olive", entry.Subject, StringComparison.OrdinalIgnoreCase);
    }
}
