using Maran.Agent.Client.Services.DbService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.DbService;
using Maran.Agent.Simulated.State;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Simulated.Tests.Services.DbService;

/// <summary>The simulated agent's database operations answer like the real agent's.</summary>
public sealed class SimulatedAgentDbClientTests
{
    /// <summary>The names a listing of the alice account returns, in order.</summary>
    private static readonly string[] ListedNames = ["alice_alpha", "alice_zeta"];

    /// <summary>A password the tests hand over; the simulation must accept it and keep nothing of it.</summary>
    private static readonly SensitiveString Password = new("correct horse battery staple");

    /// <summary>Creating a database names both the database and its user under the account.</summary>
    [Fact]
    public async Task Creating_a_database_names_the_database_and_its_user_under_the_account()
    {
        var client = NewClient();

        var created = await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("alice_shop", created.Value.DatabaseName);
        Assert.Equal("alice_shopuser", created.Value.DbUsername);
    }

    /// <summary>Creating the same database twice answers already exists and leaves the first untouched.</summary>
    [Fact]
    public async Task Creating_the_same_database_twice_answers_already_exists_and_keeps_the_first()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);

        var again = await client.CreateAsync("alice", "shop", "another", Password, CancellationToken.None);

        Assert.False(again.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, again.Error!.Code);
        var listed = await client.ListAsync("alice", CancellationToken.None);
        Assert.Single(listed.Value);
        Assert.Equal(
            SimulatedAgentErrors.NotFoundCode,
            (await client.SetPasswordAsync("alice", "another", Password, CancellationToken.None)).Error!.Code);
    }

    /// <summary>A blank or over-long name is refused as invalid input and creates nothing.</summary>
    [Theory]
    [InlineData("", "shop", "shopuser")]
    [InlineData("alice", " ", "shopuser")]
    [InlineData("alice", "shop", "")]
    public async Task A_blank_name_is_refused_as_invalid_input_and_creates_nothing(
        string account,
        string database,
        string user)
    {
        var client = NewClient();

        var created = await client.CreateAsync(account, database, user, Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, created.Error!.Code);
        Assert.Empty((await client.ListAsync(account, CancellationToken.None)).Value);
    }

    /// <summary>A name longer than the ceiling is refused as invalid input.</summary>
    [Fact]
    public async Task A_name_longer_than_the_ceiling_is_refused_as_invalid_input()
    {
        var client = NewClient();
        var tooLong = new string('a', SimulatedAgentLimits.Default.MaxNameLength + 1);

        var created = await client.CreateAsync("alice", tooLong, "shopuser", Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, created.Error!.Code);
    }

    /// <summary>Once the server holds as many databases as it will, creating another answers limit reached.</summary>
    [Fact]
    public async Task Creating_a_database_beyond_the_ceiling_answers_limit_reached()
    {
        var client = NewClient();
        for (var index = 0; index < SimulatedAgentLimits.Default.MaxDatabases; index++)
        {
            var created = await client.CreateAsync("alice", $"db{index}", "u", Password, CancellationToken.None);
            Assert.True(created.IsSuccess);
        }

        var over = await client.CreateAsync("alice", "one-too-many", "u", Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over.Error!.Code);
    }

    /// <summary>Dropping the pair removes the database and a second drop answers not found.</summary>
    [Fact]
    public async Task Dropping_the_pair_removes_the_database_and_a_second_drop_answers_not_found()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);

        var dropped = await client.DropAsync("alice", "shop", "shopuser", CancellationToken.None);
        var again = await client.DropAsync("alice", "shop", "shopuser", CancellationToken.None);

        Assert.True(dropped.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, again.Error!.Code);
        Assert.Empty((await client.ListAsync("alice", CancellationToken.None)).Value);
    }

    /// <summary>Dropping with a user that is not the database's own removes nothing.</summary>
    [Fact]
    public async Task Dropping_with_a_user_that_is_not_the_databases_own_removes_nothing()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);

        var dropped = await client.DropAsync("alice", "shop", "someone-else", CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotFoundCode, dropped.Error!.Code);
        Assert.Single((await client.ListAsync("alice", CancellationToken.None)).Value);
    }

    /// <summary>
    /// Setting the password of a user the account does not have answers not found and creates no user.
    /// </summary>
    [Fact]
    public async Task Setting_the_password_of_an_unknown_user_answers_not_found()
    {
        var client = NewClient();

        var changed = await client.SetPasswordAsync("alice", "ghost", Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotFoundCode, changed.Error!.Code);
    }

    /// <summary>
    /// Setting the password of an existing user succeeds, and another account's user of the same name is not it.
    /// </summary>
    [Fact]
    public async Task Setting_the_password_of_an_existing_user_succeeds_only_for_its_own_account()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);

        var own = await client.SetPasswordAsync("alice", "shopuser", Password, CancellationToken.None);
        var other = await client.SetPasswordAsync("bob", "shopuser", Password, CancellationToken.None);

        Assert.True(own.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, other.Error!.Code);
    }

    /// <summary>Listing returns only the account's own databases, ordered by name, with no user and no size.</summary>
    [Fact]
    public async Task Listing_returns_only_the_accounts_own_databases_in_name_order_without_user_or_size()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "zeta", "u1", Password, CancellationToken.None);
        await client.CreateAsync("alice", "alpha", "u2", Password, CancellationToken.None);
        await client.CreateAsync("bob", "beta", "u3", Password, CancellationToken.None);

        var listed = await client.ListAsync("alice", CancellationToken.None);

        Assert.Equal(ListedNames, listed.Value.Select(summary =>
        {
            return summary.DatabaseName;
        }));
        Assert.All(listed.Value, summary => { Assert.Null(summary.DbUsername); });
        Assert.All(listed.Value, summary => { Assert.Null(summary.SizeBytes); });
    }

    /// <summary>A new database reports the empty size, and a size for an unknown database answers not found.</summary>
    [Fact]
    public async Task A_new_database_reports_the_empty_size_and_an_unknown_one_answers_not_found()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);

        var known = await client.GetSizeAsync("alice", "shop", CancellationToken.None);
        var unknown = await client.GetSizeAsync("alice", "ghost", CancellationToken.None);

        Assert.Equal(SimulatedDatabaseStore.EmptyDatabaseBytes, known.Value);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, unknown.Error!.Code);
    }

    /// <summary>A size seeded into the state is the size the client reports.</summary>
    [Fact]
    public async Task A_size_seeded_into_the_state_is_the_size_the_client_reports()
    {
        var ledger = NewLedger();
        ledger.Databases.Create("alice", "big", "u", 123_456_789UL);
        var client = new SimulatedAgentDbClient(ledger);

        var size = await client.GetSizeAsync("alice", "big", CancellationToken.None);

        Assert.Equal(123_456_789UL, size.Value);
    }

    /// <summary>The grant census counts every database on the server and finds nothing to repair.</summary>
    [Fact]
    public async Task The_grant_census_counts_every_database_on_the_server_and_finds_nothing_to_repair()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "a", "u", Password, CancellationToken.None);
        await client.CreateAsync("bob", "b", "u", Password, CancellationToken.None);

        var inspected = await client.InspectGrantsAsync(CancellationToken.None);
        var repaired = await client.RepairGrantsAsync(CancellationToken.None);

        Assert.Equal(2u, inspected.Value.ExaminedGrants);
        Assert.Equal(2u, inspected.Value.AlreadyCorrect);
        Assert.Empty(inspected.Value.WouldRepair);
        Assert.Empty(inspected.Value.Refused);
        Assert.Equal(2u, repaired.Value.ExaminedGrants);
        Assert.Empty(repaired.Value.Repaired);
        Assert.Empty(repaired.Value.Refused);
    }

    /// <summary>A call whose token is already cancelled does not run.</summary>
    [Fact]
    public async Task A_call_with_a_cancelled_token_does_not_run()
    {
        var client = NewClient();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => { return client.CreateAsync("alice", "shop", "shopuser", Password, source.Token); });
        Assert.Empty((await client.ListAsync("alice", CancellationToken.None)).Value);
    }

    /// <summary>An additional user is created holding nothing and a repeat answers already exists.</summary>
    [Fact]
    public async Task An_additional_user_is_created_and_a_repeat_answers_already_exists()
    {
        var client = NewClient();

        var created = await client.CreateUserAsync("alice", "reader", Password, CancellationToken.None);
        var again = await client.CreateUserAsync("alice", "reader", Password, CancellationToken.None);

        Assert.Equal("alice_reader", created.Value);
        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, again.Error!.Code);
        Assert.True((await client.SetPasswordAsync("alice", "reader", Password, CancellationToken.None)).IsSuccess);
    }

    /// <summary>A name a database's dedicated user holds cannot be created again as an additional user.</summary>
    [Fact]
    public async Task A_name_a_dedicated_user_holds_cannot_be_created_again_as_an_additional_user()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);

        var again = await client.CreateUserAsync("alice", "shopuser", Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, again.Error!.Code);
    }

    /// <summary>Dropping an additional user removes it and a repeat answers not found.</summary>
    [Fact]
    public async Task Dropping_an_additional_user_removes_it_and_a_repeat_answers_not_found()
    {
        var client = NewClient();
        await client.CreateUserAsync("alice", "reader", Password, CancellationToken.None);

        var dropped = await client.DropUserAsync("alice", "reader", CancellationToken.None);
        var again = await client.DropUserAsync("alice", "reader", CancellationToken.None);

        Assert.True(dropped.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, again.Error!.Code);
        Assert.Equal(
            SimulatedAgentErrors.NotFoundCode,
            (await client.SetPasswordAsync("alice", "reader", Password, CancellationToken.None)).Error!.Code);
    }

    /// <summary>Privileges can be set on an existing user and database and cleared with an empty set.</summary>
    [Fact]
    public async Task Privileges_can_be_set_on_an_existing_user_and_database_and_cleared_with_an_empty_set()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);
        await client.CreateUserAsync("alice", "reader", Password, CancellationToken.None);

        var granted = await client.SetPrivilegesAsync(
            "alice", "reader", "shop", [DatabasePrivilegeKind.Select], CancellationToken.None);
        var cleared = await client.SetPrivilegesAsync("alice", "reader", "shop", [], CancellationToken.None);

        Assert.True(granted.IsSuccess);
        Assert.True(cleared.IsSuccess);
    }

    /// <summary>Privileges on a user or a database that is not there answer not found and create nothing.</summary>
    [Fact]
    public async Task Privileges_on_a_user_or_a_database_that_is_not_there_answer_not_found()
    {
        var client = NewClient();
        await client.CreateAsync("alice", "shop", "shopuser", Password, CancellationToken.None);

        var noUser = await client.SetPrivilegesAsync("alice", "ghost", "shop", [], CancellationToken.None);
        var noDatabase = await client.SetPrivilegesAsync("alice", "shopuser", "ghost", [], CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotFoundCode, noUser.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, noDatabase.Error!.Code);
        Assert.Equal(
            SimulatedAgentErrors.NotFoundCode,
            (await client.SetPasswordAsync("alice", "ghost", Password, CancellationToken.None)).Error!.Code);
    }

    /// <summary>A blank user name is refused as invalid input.</summary>
    [Fact]
    public async Task A_blank_user_name_is_refused_as_invalid_input()
    {
        var created = await NewClient().CreateUserAsync("alice", " ", Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, created.Error!.Code);
    }

    /// <summary>Builds an empty ledger with the default caps.</summary>
    /// <returns>The ledger.</returns>
    private static SimulatedAgentLedger NewLedger()
    {
        return new SimulatedAgentLedger(SimulatedAgentLimits.Default);
    }

    /// <summary>Builds a client over a fresh ledger.</summary>
    /// <returns>The client.</returns>
    private static SimulatedAgentDbClient NewClient()
    {
        return new SimulatedAgentDbClient(NewLedger());
    }
}
