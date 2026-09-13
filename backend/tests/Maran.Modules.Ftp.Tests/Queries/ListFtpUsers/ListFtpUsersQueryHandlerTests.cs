using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Queries.ListFtpUsers;
using Maran.Modules.Ftp.Tests.TestSupport;

namespace Maran.Modules.Ftp.Tests.Queries.ListFtpUsers;

/// <summary>
/// Listing a customer's FTPS logins: what the answer contains, whose rows are in it, and the two
/// things it must never do — read the host's user database, or count a login the panel does not own.
/// </summary>
public sealed class ListFtpUsersQueryHandlerTests
{
    /// <summary>The account whose logins the caller in these tests owns.</summary>
    private static readonly Guid CustomerA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose logins must not appear in the answer.</summary>
    private static readonly Guid CustomerB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The listing carries the caller's own logins and nothing else.</summary>
    /// <remarks>
    /// Both halves in one assertion: the caller's row is present — so the query is not merely
    /// returning nothing — and the neighbour's is absent. A test that only asserted the absence
    /// would pass on a handler that returned an empty list forever.
    /// </remarks>
    [Fact]
    public async Task The_listing_carries_the_callers_own_logins_and_no_neighbours()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = Context(CustomerB, databaseName);
        await SeedAsync(owner, CustomerB, "beta", "secrets");

        using var context = Context(CustomerA, databaseName);
        await SeedAsync(context, CustomerA, "acme", "files");

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var login = Assert.Single(result.Value);
        Assert.Equal("files", login.Name);
        Assert.Equal("acme_files", login.FullName);
        Assert.Equal(CustomerA, login.AccountId);
    }

    /// <summary>Every listed login carries the protocol label the backend owns.</summary>
    /// <remarks>
    /// The merged "File transfer" screen renders logins from two modules and must be told which
    /// daemon each belongs to by the server. A row arriving without the token is rendered by the SPA
    /// as whatever the caller declared the endpoint answers by construction, which is exactly the
    /// guess this field exists to remove.
    /// </remarks>
    [Fact]
    public async Task Every_listed_login_carries_the_backend_owned_protocol_label()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        await SeedAsync(context, CustomerA, "acme", "files");
        await SeedAsync(context, CustomerA, "acme", "deploy");

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, login =>
        {
            Assert.Equal("Ftps", login.Protocol);
        });
    }

    /// <summary>The listing cannot reach the agent, because no handler here is given one.</summary>
    /// <remarks>
    /// <para>
    /// A property held by the TYPE rather than by a runtime assertion, and stated as such: the
    /// handler's constructor takes the database context and nothing else, so a listing that decided
    /// to enumerate the host's users would not compile. That is a stronger arrangement than a test —
    /// but it is invisible in a folder listing, so it is asserted here by reflection over the
    /// constructor's parameters, which is the one form in which it CAN be observed.
    /// </para>
    /// <para>
    /// What it protects: the host has no notion of a tenant, so choosing what to show from
    /// <c>/etc/passwd</c> means matching a prefix — and <c>alice_</c> is a prefix of
    /// <c>alice_bob</c>'s logins too. That listing would disclose a neighbouring account's logins as
    /// the caller's own.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_listing_handler_is_not_given_an_agent_client_and_so_cannot_read_the_hosts_users()
    {
        var parameters = typeof(ListFtpUsersQueryHandler)
            .GetConstructors()
            .SelectMany(constructor =>
            {
                return constructor.GetParameters();
            })
            .Select(parameter =>
            {
                return parameter.ParameterType;
            })
            .ToList();

        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(typeof(IAgentFtpsClient), parameters);
    }

    /// <summary>Builds a context seen by one customer, over a named database two contexts can share.</summary>
    /// <param name="accountId">The account the principal owns.</param>
    /// <param name="databaseName">The in-memory database to attach to.</param>
    /// <returns>The assembled module, seen as that customer.</returns>
    private static FtpTestContext Context(Guid accountId, string databaseName)
    {
        return new FtpTestContext(new CustomerCurrentUser(accountId), databaseName);
    }

    /// <summary>Writes one login for an account straight into the database.</summary>
    /// <param name="context">The assembled module whose database receives the row.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name, which prefixes the login.</param>
    /// <param name="name">The login's suffix.</param>
    private static async Task SeedAsync(FtpTestContext context, Guid accountId, string username, string name)
    {
        context.Database.FtpUsers.Add(
            new FtpUser(Guid.NewGuid(), accountId, name, $"{username}_{name}", context.Clock.UtcNow));
        await context.Database.SaveChangesAsync(CancellationToken.None);
    }
}
