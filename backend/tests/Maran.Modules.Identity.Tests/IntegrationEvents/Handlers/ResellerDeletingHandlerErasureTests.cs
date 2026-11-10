using System.Text.RegularExpressions;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What deleting a reseller does to the audit journal: the reseller's login is the third path that
/// deletes a login, so it pseudonymises in the same transaction like the other two.
/// </summary>
public sealed class ResellerDeletingHandlerErasureTests : IDisposable
{
    /// <summary>The address of the reseller's own rows, distinct from every other row's.</summary>
    private const string ResellerAddress = "198.51.100.77";

    /// <summary>The user agent of the reseller's own rows, distinct from every other row's.</summary>
    private const string ResellerAgent = "reseller-browser/2";

    /// <summary>The reseller being deleted.</summary>
    private static readonly Guid ResellerId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");

    /// <summary>The database every test seeds and reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The journal double the handler writes its summary into.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The reseller's login.</summary>
    private readonly User _login = User.InviteReseller(
        Guid.NewGuid(), "northwind", "ops@northwind.test", ResellerId, ErasureHandlers.Now);

    /// <summary>The administrator who acts on the reseller.</summary>
    private readonly User _admin = new(
        Guid.NewGuid(), "administrator", "admin@example.test", "hash", UserRole.Admin, ErasureHandlers.Now);

    /// <summary>Releases the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Deleting a reseller leaves no original identifier of the login in the journal, and the administrator's
    /// own rows keep their actor; the search first finds the originals in the seeded rows.
    /// </summary>
    [Fact]
    public async Task Deleting_a_reseller_leaves_no_original_identifier_of_its_login_in_the_journal()
    {
        await SeedAsync();
        var needles = new[] { _login.Id.ToString("D"), "northwind", "ops@northwind.test", ResellerAddress, ResellerAgent };
        Assert.True(ErasureJournal.Mentions(await ErasureJournal.AllAsync(_context), needles));

        await ErasureHandlers.ResellerDeleting(_context, _audit).HandleAsync(
            new ResellerDeleting(ResellerId), CancellationToken.None);

        var rows = await ErasureJournal.AllAsync(_context);
        Assert.False(ErasureJournal.Mentions(rows, needles));
        Assert.All(rows.Where(row => { return row.ActorUserId == _admin.Id; }), row =>
        {
            Assert.Equal("administrator", row.ActorUsername);
        });
        Assert.Equal(2, rows.Count);
    }

    /// <summary>One summary is written, after the removal, holding the pseudonym and a count only.</summary>
    [Fact]
    public async Task Deleting_a_reseller_writes_one_summary_with_a_pseudonym_and_a_count()
    {
        await SeedAsync();

        await ErasureHandlers.ResellerDeleting(_context, _audit).HandleAsync(
            new ResellerDeleting(ResellerId), CancellationToken.None);

        var summary = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.PersonalDataErased, summary.Action);
        Assert.Equal("maran-identity", summary.ActorUsername);
        Assert.Matches(new Regex("^erased-user-[0-9a-f]{8} rows=2$"), summary.Subject);
    }

    /// <summary>A reseller with no login erases nothing and writes nothing.</summary>
    [Fact]
    public async Task Deleting_a_reseller_with_no_login_erases_nothing_and_writes_nothing()
    {
        _context.AuditEvents.Add(ErasureJournal.Row(_admin.Id, "administrator", "northwind"));
        await _context.SaveChangesAsync();

        await ErasureHandlers.ResellerDeleting(_context, _audit).HandleAsync(
            new ResellerDeleting(ResellerId), CancellationToken.None);

        Assert.Null(Assert.Single(await ErasureJournal.AllAsync(_context)).ErasedAt);
        Assert.Empty(_audit.Written);
    }

    /// <summary>A deletion the database refuses writes no summary, because the summary follows the commit.</summary>
    /// <remarks>
    /// UNOBSERVED HERE: the rollback itself. The in-memory provider keeps no transaction, so this asserts
    /// the order and not the rollback.
    /// </remarks>
    [Fact]
    public async Task A_deletion_the_database_refuses_writes_no_summary()
    {
        using var refusing = RefusingIdentityTestContext.Create();
        refusing.Users.AddRange(_login, _admin);
        refusing.AuditEvents.Add(ErasureJournal.Row(_login.Id, "northwind", "site", ResellerAddress, ResellerAgent));
        await refusing.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await ErasureHandlers.ResellerDeleting(refusing, _audit).HandleAsync(
                new ResellerDeleting(ResellerId), CancellationToken.None);
        });

        Assert.Empty(_audit.Written);
    }

    /// <summary>Stores the reseller's login, the administrator, and two journal rows about the login: its own, and the administrator's about it (carrying the login it is about).</summary>
    /// <returns>Resolves once everything is stored.</returns>
    private async Task SeedAsync()
    {
        _login.Activate("hash");
        _context.Users.AddRange(_login, _admin);
        _context.AuditEvents.AddRange(
            ErasureJournal.Row(_login.Id, "northwind", "reseller-site", ResellerAddress, ResellerAgent),
            ErasureJournal.Row(_admin.Id, "administrator", "ops@northwind.test", subjectUserId: _login.Id));
        await _context.SaveChangesAsync();
    }
}
