using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// The rows a view-as test needs: an administrator holding an own session, and an account owned by a
/// customer login, all in one in-memory Identity context.
/// </summary>
public sealed class ViewAsFixture : IDisposable
{
    /// <summary>The instant every test starts at.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The Identity context.</summary>
    public IdentityDbContext Context { get; }

    /// <summary>The panel clock every service reads.</summary>
    public FakeClock Clock { get; }

    /// <summary>The account directory, answering only accounts added to it.</summary>
    public StubAccountDirectory Accounts { get; }

    /// <summary>Every journal entry written.</summary>
    public RecordingAuditWriter Audit { get; }

    /// <summary>A journal writing into <see cref="Audit"/>.</summary>
    public IdentityAuditJournal Journal
    {
        get
        {
            return new IdentityAuditJournal(Audit, new StubCurrentUser());
        }
    }

    /// <summary>Creates the context, the clock and the directory, with no rows yet.</summary>
    public ViewAsFixture()
    {
        Context = IdentityTestContext.Create();
        Clock = new FakeClock(Now);
        Accounts = new StubAccountDirectory();
        Audit = new RecordingAuditWriter();
    }

    /// <summary>Creates a real token issuer with a fixed test key.</summary>
    /// <returns>The issuer.</returns>
    public JwtAccessTokenIssuer NewIssuer()
    {
        var options = new JwtOptions
        {
            SigningKey = Convert.ToBase64String(new byte[32]),
            Issuer = "maran",
            Audience = "maran-panel",
            AccessTokenMinutes = 15,
        };

        return new JwtAccessTokenIssuer(new OptionsWrapper<JwtOptions>(options), TestSecurityPolicyCache.Over(Context), Clock);
    }

    /// <summary>Seeds an administrator and one own session of theirs.</summary>
    /// <param name="username">The administrator's login name.</param>
    /// <returns>The administrator and their session.</returns>
    public async Task<(User Admin, Session Session)> SeedAdminAsync(string username = "root-admin")
    {
        var admin = new User(Guid.NewGuid(), username, $"{username}@example.com", "hash", UserRole.Admin, Now);
        var session = new Session(Guid.NewGuid(), admin.Id, Guid.NewGuid(), Guid.NewGuid().ToString("N"), Now, Now.AddDays(14), "198.51.100.1", "admin-agent");
        Context.Users.Add(admin);
        Context.Sessions.Add(session);
        await Context.SaveChangesAsync();
        return (admin, session);
    }

    /// <summary>Seeds an account in the directory and the customer login that owns it.</summary>
    /// <param name="accountName">The account's name.</param>
    /// <returns>The account id and its owning login.</returns>
    public async Task<(Guid AccountId, User Owner)> SeedAccountAsync(string accountName = "acme")
    {
        var accountId = Guid.NewGuid();
        Accounts.Add(accountId, accountName, maxFtpUsers: 0);

        var owner = new User(Guid.NewGuid(), $"{accountName}-owner", $"{accountName}@example.com", "hash", UserRole.Customer, Now);
        Context.Users.Add(owner);
        Context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), owner.Id, accountId, Now));
        await Context.SaveChangesAsync();
        return (accountId, owner);
    }

    /// <summary>Releases the context.</summary>
    public void Dispose()
    {
        Context.Dispose();
    }
}
