using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Queries.ListFtpUsers;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.SharedKernel.Pagination;

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

    /// <summary>The instant the paging tests' logins are created at, a day apart from one another.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
        var login = Assert.Single(result.Value.Items);
        Assert.Equal("files", login.Name);
        Assert.Equal("acme_files", login.FullName);
        Assert.Equal(CustomerA, login.AccountId);
    }

    /// <summary>Every listed login carries the protocol label the backend owns.</summary>
    /// <remarks>
    /// A client must be told which daemon each login belongs to by the server, not left to infer it
    /// from which endpoint answered: the inference is exactly the guess this field exists to remove.
    /// </remarks>
    [Fact]
    public async Task Every_listed_login_carries_the_backend_owned_protocol_label()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        await SeedAsync(context, CustomerA, "acme", "files");
        await SeedAsync(context, CustomerA, "acme", "deploy");

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, login =>
        {
            Assert.Equal("Ftps", login.Protocol);
        });
    }

    /// <summary>The listing cannot reach the agent, because no handler here is given one.</summary>
    /// <remarks>
    /// <para>
    /// A property held by the TYPE rather than by a runtime assertion, and stated as such: the
    /// handler's constructor takes the database context and the account-name directory and no agent
    /// client, so a listing that decided to enumerate the host's users would not compile. That is a stronger arrangement than a test —
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


    /// <summary>An administrator naming one account is listed only that account's FTPS logins.</summary>
    /// <remarks>
    /// Run as an administrator, whom no tenant filter narrows, so the other account's row is visible
    /// to the context and only the account filter can remove it.
    /// </remarks>
    [Fact]
    public async Task An_administrator_naming_one_account_is_listed_only_that_accounts_logins()
    {
        using var context = new FtpTestContext(new TestCurrentUser(), Guid.NewGuid().ToString());
        await SeedAsync(context, CustomerA, "acme", "files");
        await SeedAsync(context, CustomerB, "beta", "secrets");

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(AccountId: CustomerB), CancellationToken.None);

        var login = Assert.Single(result.Value.Items);
        Assert.Equal("beta_secrets", login.FullName);
    }

    /// <summary>A customer naming another account is listed nothing.</summary>
    /// <remarks>
    /// The account filter is ANDed with the tenant filter, never in place of it: the customer's own
    /// login exists and is not returned either, because it is not the account they named.
    /// </remarks>
    [Fact]
    public async Task A_customer_naming_another_account_is_listed_nothing()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = Context(CustomerB, databaseName);
        await SeedAsync(owner, CustomerB, "beta", "secrets");

        using var context = Context(CustomerA, databaseName);
        await SeedAsync(context, CustomerA, "acme", "files");

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(AccountId: CustomerB), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    /// <summary>
    /// A listing labels each row with its owner's name from the account directory, in one batched
    /// lookup, and leaves the name empty for an account the directory does not name.
    /// </summary>
    [Fact]
    public async Task Rows_carry_the_owner_name_the_directory_answers_and_empty_for_one_it_does_not()
    {
        using var context = new FtpTestContext(new TestCurrentUser(), Guid.NewGuid().ToString());
        await SeedAsync(context, CustomerA, "acme", "files");
        await SeedAsync(context, CustomerB, "beta", "secrets");

        // The directory names CustomerA and not CustomerB — exactly how the real one answers for an
        // account that was deleted or that the caller may not see.
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", result.Value.Items.Single(row =>
        {
            return row.AccountId == CustomerA;
        }).OwnerName);
        Assert.Equal(string.Empty, result.Value.Items.Single(row =>
        {
            return row.AccountId == CustomerB;
        }).OwnerName);

        var batch = Assert.Single(context.Accounts.NameLookups);
        Assert.Equal(
            new[] { CustomerA, CustomerB }.Order(),
            batch.Order());
    }

    /// <summary>With no sort named, logins are listed oldest first.</summary>
    [Fact]
    public async Task With_no_sort_named_logins_are_listed_oldest_first()
    {
        // Inserted out of order so the assertion cannot pass by insertion-order accident.
        using var context = new FtpTestContext(new TestCurrentUser(), Guid.NewGuid().ToString());
        await SeedLoginsAsync(context, ("second", 1), ("first", 0), ("third", 2));

        var result = await context.ListUsersHandler.HandleAsync(new ListFtpUsersQuery(), CancellationToken.None);

        Assert.Equal(["acme_first", "acme_second", "acme_third"], FullNames(result.Value.Items));
    }

    /// <summary>A descending name sort orders the page by the login the host holds, last first.</summary>
    [Fact]
    public async Task A_descending_name_sort_orders_by_the_login_the_host_holds_last_first()
    {
        using var context = new FtpTestContext(new TestCurrentUser(), Guid.NewGuid().ToString());
        await SeedLoginsAsync(context, ("bravo", 0), ("charlie", 1), ("alpha", 2));

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(Sort: SortRequest.Parse("-name")), CancellationToken.None);

        Assert.Equal(["acme_charlie", "acme_bravo", "acme_alpha"], FullNames(result.Value.Items));
    }

    /// <summary>The second page holds the rows after the first, and the total counts every match.</summary>
    [Fact]
    public async Task The_second_page_holds_the_rows_after_the_first_and_the_total_counts_every_match()
    {
        using var context = new FtpTestContext(new TestCurrentUser(), Guid.NewGuid().ToString());
        await SeedLoginsAsync(context, [.. Enumerable.Range(0, 12).Select(day =>
        {
            return ($"login{day:D2}", day);
        })]);

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(10, result.Value.PageSize);
        Assert.Equal(["acme_login10", "acme_login11"], FullNames(result.Value.Items));
    }

    /// <summary>A page past the last one answers empty with the true total and the page asked for.</summary>
    [Fact]
    public async Task A_page_past_the_last_one_answers_empty_with_the_true_total_and_the_page_asked_for()
    {
        using var context = new FtpTestContext(new TestCurrentUser(), Guid.NewGuid().ToString());
        await SeedAsync(context, CustomerA, "acme", "files");
        await SeedAsync(context, CustomerB, "beta", "secrets");

        var result = await context.ListUsersHandler.HandleAsync(
            new ListFtpUsersQuery(Page: 5, PageSize: 10), CancellationToken.None);

        Assert.Empty(result.Value.Items);
        Assert.Equal(2, result.Value.Total);
        Assert.Equal(5, result.Value.Page);
    }

    /// <summary>A customer's total counts only their own logins, never another tenant's.</summary>
    [Fact]
    public async Task A_customers_total_counts_only_their_own_logins()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = Context(CustomerB, databaseName);
        await SeedAsync(owner, CustomerB, "beta", "secrets");

        using var context = Context(CustomerA, databaseName);
        await SeedAsync(context, CustomerA, "acme", "files");

        var result = await context.ListUsersHandler.HandleAsync(new ListFtpUsersQuery(), CancellationToken.None);

        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>Seeds logins for <see cref="CustomerA"/>, each created the given days after the epoch.</summary>
    /// <param name="context">The assembled module whose database receives the rows.</param>
    /// <param name="logins">Each login's suffix and its creation day.</param>
    private static async Task SeedLoginsAsync(FtpTestContext context, params (string Name, int Day)[] logins)
    {
        foreach (var (name, day) in logins)
        {
            context.Database.FtpUsers.Add(
                new FtpUser(Guid.NewGuid(), CustomerA, name, $"acme_{name}", Epoch.AddDays(day)));
        }

        await context.Database.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>The full names on a page, in order.</summary>
    /// <param name="rows">The page's rows.</param>
    /// <returns>Each row's full name.</returns>
    private static List<string> FullNames(IEnumerable<FtpUserDto> rows)
    {
        return rows
            .Select(row =>
            {
                return row.FullName;
            })
            .ToList();
    }
}
