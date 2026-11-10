using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// The rows a view-as test needs: an administrator holding an own session, an account owned by a
/// customer login and a reseller with its login, all in one in-memory Identity context.
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

    /// <summary>Which reseller owns which account right now, answering the view-as ownership check.</summary>
    public StubAccountOwnership Ownership { get; }

    /// <summary>The reseller directory, answering only resellers added to it.</summary>
    public StubResellerDirectory Resellers { get; }

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
        Resellers = new StubResellerDirectory();
        Ownership = new StubAccountOwnership();
        Audit = new RecordingAuditWriter();
    }

    /// <summary>Creates the one starter both view-as start handlers share, over a real token issuer.</summary>
    /// <returns>The starter.</returns>
    public ViewAsStarter NewStarter()
    {
        return new ViewAsStarter(Context, Resellers, NewIssuer(), Journal, Clock);
    }

    /// <summary>Creates the per-request check of a view-as session over this fixture's ownership.</summary>
    /// <returns>The verifier.</returns>
    public ViewAsSessionVerifier NewVerifier()
    {
        return new ViewAsSessionVerifier(Context, Clock, Ownership);
    }

    /// <summary>Creates the reader of what a view-as session looks at.</summary>
    /// <returns>The reader.</returns>
    public ViewAsSubjectReader NewSubjectReader()
    {
        return new ViewAsSubjectReader(Context, Accounts, Resellers);
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
    /// <param name="resellerId">The reseller that owns the account, or null for an administrator's.</param>
    /// <returns>The account id and its owning login.</returns>
    public async Task<(Guid AccountId, User Owner)> SeedAccountAsync(string accountName = "acme", Guid? resellerId = null)
    {
        var accountId = Guid.NewGuid();
        Accounts.Add(accountId, accountName, maxFtpUsers: 0, resellerId: resellerId);
        if (resellerId is { } owningReseller)
        {
            Ownership.Own(accountId, owningReseller);
        }

        var owner = new User(Guid.NewGuid(), $"{accountName}-owner", $"{accountName}@example.com", "hash", UserRole.Customer, Now);
        Context.Users.Add(owner);
        Context.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), owner.Id, accountId, Now));
        await Context.SaveChangesAsync();
        return (accountId, owner);
    }

    /// <summary>Seeds a reseller in the directory and the one login it signs in with.</summary>
    /// <param name="resellerName">The reseller's name, which is also its login name.</param>
    /// <param name="state">Whether the reseller is in service or suspended.</param>
    /// <param name="claimed">Whether the login has set its password; false leaves it waiting for its invitation.</param>
    /// <returns>The reseller id and its login.</returns>
    public async Task<(Guid ResellerId, User Login)> SeedResellerAsync(
        string resellerName = "northwind",
        ResellerState state = ResellerState.Active,
        bool claimed = true)
    {
        var resellerId = Guid.NewGuid();
        Resellers.Add(resellerId, resellerName, state);

        var login = User.InviteReseller(Guid.NewGuid(), resellerName, $"{resellerName}@example.com", resellerId, Now);
        if (claimed)
        {
            login.Activate("hash");
        }

        Context.Users.Add(login);
        await Context.SaveChangesAsync();
        return (resellerId, login);
    }

    /// <summary>Seeds a reseller with its login and one own session of that login.</summary>
    /// <param name="resellerName">The reseller's name, which is also its login name.</param>
    /// <param name="state">Whether the reseller is in service or suspended.</param>
    /// <returns>The reseller id, its login and the login's own session.</returns>
    public async Task<(Guid ResellerId, User Login, Session Session)> SeedResellerOperatorAsync(
        string resellerName = "northwind",
        ResellerState state = ResellerState.Active)
    {
        var (resellerId, login) = await SeedResellerAsync(resellerName, state);
        var session = new Session(Guid.NewGuid(), login.Id, Guid.NewGuid(), Guid.NewGuid().ToString("N"), Now, Now.AddDays(14), "198.51.100.2", "reseller-agent");
        Context.Sessions.Add(session);
        await Context.SaveChangesAsync();
        return (resellerId, login, session);
    }

    /// <summary>Stores a view-as session of <paramref name="viewed"/> started by <paramref name="actor"/>, as the starter would.</summary>
    /// <param name="actor">The operator viewing.</param>
    /// <param name="actorSession">The operator's own session.</param>
    /// <param name="viewed">The login being viewed as.</param>
    /// <param name="accountId">The viewed account, or null for a reseller view.</param>
    /// <returns>The stored view-as session.</returns>
    public async Task<Session> AddViewAsSessionAsync(User actor, Session actorSession, User viewed, Guid? accountId)
    {
        var viewAs = Session.StartViewAs(
            Guid.NewGuid(),
            viewed.Id,
            accountId,
            actor.Id,
            actorSession.Id,
            Guid.NewGuid().ToString("N"),
            Now,
            "198.51.100.1",
            "operator-agent");
        Context.Sessions.Add(viewAs);
        await Context.SaveChangesAsync();
        return viewAs;
    }

    /// <summary>Releases the context.</summary>
    public void Dispose()
    {
        Context.Dispose();
    }
}
