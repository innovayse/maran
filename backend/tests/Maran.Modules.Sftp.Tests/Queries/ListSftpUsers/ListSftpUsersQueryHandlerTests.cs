using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Queries.ListSftpUsers;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Sftp.Tests.Queries.ListSftpUsers;

/// <summary>Where a listing's rows come from, and — just as importantly — where they do not.</summary>
public sealed class ListSftpUsersQueryHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>The instant the paging tests' logins are created at, a day apart from one another.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Listing sftp users returns the panels own tenant rows and never asks the agent to enumerate.</summary>
    [Fact]
    public async Task Listing_sftp_users_returns_the_panels_own_tenant_rows_and_never_asks_the_agent_to_enumerate()
    {
        // The host has no notion of a tenant, so an answer derived from /etc/passwd is derived from
        // a prefix scan — and `alice_` is a prefix of account `alice_bob`'s logins too, because
        // account names may contain the separator. Authorisation is the panel's own tenant-filtered
        // rows.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["deploy"], result.Value.Items.Select(row =>
        {
            return row.Name;
        }));

        // Asserted STRUCTURALLY, over the type rather than over a call counter on a double the
        // handler was never given. A counter on an unwired double reads as proof and is not: it
        // stays at zero however the handler is written, so it could never have gone red. What can go
        // red is this — the handler cannot ask the agent anything, because it cannot be handed one.
        Assert.DoesNotContain(
            typeof(ListSftpUsersQueryHandler).GetConstructors()
                .SelectMany(constructor =>
                {
                    return constructor.GetParameters();
                }),
            parameter =>
            {
                return parameter.ParameterType == typeof(IAgentSftpClient);
            });
    }

    /// <summary>No query handler in this module can be handed the agent at all.</summary>
    [Fact]
    public void No_query_handler_in_this_module_can_be_handed_the_agent_at_all()
    {
        // The same guarantee widened past the one handler the test above names, so a NEW read that
        // reached for the host's own user database would fail here rather than ship. Reads in this
        // module are answered from the panel's tenant-filtered rows and from nothing else.
        var readers = typeof(ListSftpUsersQueryHandler).Assembly.GetTypes()
            .Where(type =>
            {
                return type.Namespace?.StartsWith(
                    "Maran.Modules.Sftp.Queries", StringComparison.Ordinal) == true;
            })
            .Where(type =>
            {
                return type.GetConstructors().SelectMany(constructor =>
                {
                    return constructor.GetParameters();
                }).Any(parameter =>
                {
                    return parameter.ParameterType == typeof(IAgentSftpClient);
                });
            })
            .Select(type =>
            {
                return type.Name;
            })
            .ToList();

        Assert.True(
            readers.Count == 0,
            "A read in this module must be answered from the panel's own rows, never from the host's "
            + "own user database: " + string.Join(", ", readers));
    }

    /// <summary>A listing shows a customer only their own rows.</summary>
    [Fact]
    public async Task A_listing_shows_a_customer_only_their_own_rows()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(StrangerAccountId), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.Equal(["backup"], result.Value.Items.Select(row =>
        {
            return row.Name;
        }));
    }

    /// <summary>An administrator sees every accounts rows.</summary>
    [Fact]
    public async Task An_administrator_sees_every_accounts_rows()
    {
        // Guards the two tests above from passing for the wrong reason: if the seed never ran, or the
        // handler simply returned nothing, "only their own" would be true of an empty result.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Items.Count);
    }

    /// <summary>A listing carries the login a customer signs in with and no password.</summary>
    [Fact]
    public async Task A_listing_carries_the_login_a_customer_signs_in_with_and_no_password()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal("alice_deploy", row.FullName);

        // Structural, not a spot check: the DTO must have no password-shaped member at all.
        Assert.DoesNotContain(
            typeof(Modules.Sftp.Common.SftpUserDto).GetProperties(),
            property =>
            {
                return property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Every listed row states the protocol it is a login for.</summary>
    [Fact]
    public async Task Every_listed_row_states_the_protocol_it_is_a_login_for()
    {
        // Asked of the LISTING as well as of the single read: a label present on one path and absent
        // on the other is a wire contract that depends on which URL a client happened to call.
        //
        // The literal token, and never the constant that produced it: asserting against
        // SftpProtocolName.Sftp would survive a rename of the constant, while a client matches the
        // spelling. And never the empty string, which would leave the fact to be inferred — the very
        // thing the member exists to replace.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(result.Value.Items, row =>
        {
            Assert.Equal("Sftp", row.Protocol);
        });
    }

    /// <summary>Seeds one login for each of the two accounts into a shared store.</summary>
    /// <param name="databaseName">The shared in-memory database.</param>
    private static async Task SeedAsync(string databaseName)
    {
        // Written through a context whose principal is an administrator, so the seed itself does no
        // tenant separating — the filter under test is the only thing that can.
        using var seed = SftpTestContext.Create(FakeCurrentUser.Admin(), databaseName);
        seed.SftpUsers.Add(SftpTestContext.Row(OwnerAccountId, "alice", "deploy"));
        seed.SftpUsers.Add(SftpTestContext.Row(StrangerAccountId, "bob", "backup"));
        await seed.SaveChangesAsync();
    }


    /// <summary>An administrator naming one account is listed only that account's SFTP logins.</summary>
    /// <remarks>
    /// Run as an administrator, whom no tenant filter narrows, so the other account's row is visible
    /// to the context and only the account filter can remove it.
    /// </remarks>
    [Fact]
    public async Task An_administrator_naming_one_account_is_listed_only_that_accounts_sftp_logins()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(AccountId: StrangerAccountId), CancellationToken.None);

        Assert.Equal(["backup"], result.Value.Items.Select(row =>
        {
            return row.Name;
        }));
    }

    /// <summary>A customer naming another account is listed nothing.</summary>
    /// <remarks>
    /// The account filter is ANDed with the tenant filter, never in place of it: the customer's own
    /// row exists and is not returned either, because it is not the account they named.
    /// </remarks>
    [Fact]
    public async Task A_customer_naming_another_account_is_listed_nothing()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(AccountId: StrangerAccountId), CancellationToken.None);

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
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        // The directory names the owner and not the stranger — exactly how the real one answers for
        // an account that was deleted or that the caller may not see.
        var accounts = new StubAccountDirectory(
            new AccountSnapshot(OwnerAccountId, "alice", 5, 5, 5, 5, 5, 1_024, AccountState.Active));

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context, accounts)
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", result.Value.Items.Single(row =>
        {
            return row.AccountId == OwnerAccountId;
        }).OwnerName);
        Assert.Equal(string.Empty, result.Value.Items.Single(row =>
        {
            return row.AccountId == StrangerAccountId;
        }).OwnerName);

        var batch = Assert.Single(accounts.NameLookups);
        Assert.Equal(
            new[] { OwnerAccountId, StrangerAccountId }.Order(),
            batch.Order());
    }

    /// <summary>With no sort named, logins are listed oldest first.</summary>
    [Fact]
    public async Task With_no_sort_named_logins_are_listed_oldest_first()
    {
        // Inserted out of order so the assertion cannot pass by insertion-order accident.
        var shared = Guid.NewGuid().ToString();
        await SeedLoginsAsync(shared, ("second", 1), ("first", 0), ("third", 2));

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.Equal(["alice_first", "alice_second", "alice_third"], FullNames(result.Value.Items));
    }

    /// <summary>A descending name sort orders the page by the login the host holds, last first.</summary>
    [Fact]
    public async Task A_descending_name_sort_orders_by_the_login_the_host_holds_last_first()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedLoginsAsync(shared, ("bravo", 0), ("charlie", 1), ("alpha", 2));

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(Sort: SortRequest.Parse("-name")), CancellationToken.None);

        Assert.Equal(["alice_charlie", "alice_bravo", "alice_alpha"], FullNames(result.Value.Items));
    }

    /// <summary>The second page holds the rows after the first, and the total counts every match.</summary>
    [Fact]
    public async Task The_second_page_holds_the_rows_after_the_first_and_the_total_counts_every_match()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedLoginsAsync(shared, [.. Enumerable.Range(0, 12).Select(day =>
        {
            return ($"login{day:D2}", day);
        })]);

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(10, result.Value.PageSize);
        Assert.Equal(["alice_login10", "alice_login11"], FullNames(result.Value.Items));
    }

    /// <summary>A page past the last one answers empty with the true total and the page asked for.</summary>
    [Fact]
    public async Task A_page_past_the_last_one_answers_empty_with_the_true_total_and_the_page_asked_for()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(Page: 5, PageSize: 10), CancellationToken.None);

        Assert.Empty(result.Value.Items);
        Assert.Equal(2, result.Value.Total);
        Assert.Equal(5, result.Value.Page);
    }

    /// <summary>A customer's total counts only their own logins, never another tenant's.</summary>
    [Fact]
    public async Task A_customers_total_counts_only_their_own_logins()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListSftpUsersQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>Seeds logins for the owner account, each created the given number of days after the epoch.</summary>
    /// <param name="databaseName">The shared in-memory database.</param>
    /// <param name="logins">Each login's suffix and its creation day.</param>
    private static async Task SeedLoginsAsync(string databaseName, params (string Name, int Day)[] logins)
    {
        using var seed = SftpTestContext.Create(FakeCurrentUser.Admin(), databaseName);
        foreach (var (name, day) in logins)
        {
            seed.SftpUsers.Add(new SftpUser(Guid.NewGuid(), OwnerAccountId, name, $"alice_{name}", Epoch.AddDays(day)));
        }

        await seed.SaveChangesAsync();
    }

    /// <summary>The full names on a page, in order.</summary>
    /// <param name="rows">The page's rows.</param>
    /// <returns>Each row's full name.</returns>
    private static List<string> FullNames(IEnumerable<Modules.Sftp.Common.SftpUserDto> rows)
    {
        return rows
            .Select(row =>
            {
                return row.FullName;
            })
            .ToList();
    }
}
