using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.SftpService;
using Maran.Agent.Simulated.State;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Simulated.Tests.Services.SftpService;

/// <summary>The simulated agent's SFTP login operations answer like the real agent's.</summary>
public sealed class SimulatedAgentSftpClientTests
{
    /// <summary>The one key line the tests install.</summary>
    private static readonly string[] InstalledKeys = [KeyLine];

    /// <summary>The one short key line a replaced list keeps.</summary>
    private static readonly string[] SingleKey = ["ssh-ed25519 only"];

    /// <summary>A key line the tests install; the simulation checks its size, not its cryptography.</summary>
    private const string KeyLine = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExample user@laptop";

    /// <summary>A password the tests hand over; the simulation accepts it and keeps nothing of it.</summary>
    private static readonly SensitiveString Password = new("correct horse battery staple");

    /// <summary>Creating a login names it under the account.</summary>
    [Fact]
    public async Task Creating_a_login_names_it_under_the_account()
    {
        var (client, _) = NewClient();

        var created = await client.CreateAsync("alice", "deploy", Password, CancellationToken.None);

        Assert.Equal("alice_deploy", created.Value);
    }

    /// <summary>Creating a login that exists answers already exists and keeps the keys it holds.</summary>
    [Fact]
    public async Task Creating_a_login_that_exists_answers_already_exists_and_keeps_its_keys()
    {
        var (client, ledger) = NewClient();
        await client.CreateAsync("alice", "deploy", Password, CancellationToken.None);
        await client.SetAuthorizedKeysAsync("alice", "deploy", [KeyLine], CancellationToken.None);

        var again = await client.CreateAsync("alice", "deploy", Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, again.Error!.Code);
        Assert.Equal(InstalledKeys, ledger.Logins.Find("alice", "deploy")!.PublicKeys);
    }

    /// <summary>Two accounts may each hold a login of the same suffix.</summary>
    [Fact]
    public async Task Two_accounts_may_each_hold_a_login_of_the_same_suffix()
    {
        var (client, _) = NewClient();

        var first = await client.CreateAsync("alice", "deploy", Password, CancellationToken.None);
        var second = await client.CreateAsync("bob", "deploy", Password, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
    }

    /// <summary>A blank suffix is refused as invalid input.</summary>
    [Fact]
    public async Task A_blank_suffix_is_refused_as_invalid_input()
    {
        var (client, _) = NewClient();

        var created = await client.CreateAsync("alice", " ", Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, created.Error!.Code);
    }

    /// <summary>Once the server holds as many logins as it will, creating another answers limit reached.</summary>
    [Fact]
    public async Task Creating_a_login_beyond_the_ceiling_answers_limit_reached()
    {
        var (client, _) = NewClient();
        for (var index = 0; index < SimulatedAgentLimits.Default.MaxSftpLogins; index++)
        {
            Assert.True((await client.CreateAsync("alice", $"l{index}", Password, CancellationToken.None)).IsSuccess);
        }

        var over = await client.CreateAsync("alice", "one-too-many", Password, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over.Error!.Code);
    }

    /// <summary>Setting a password needs the login to exist and creates none.</summary>
    [Fact]
    public async Task Setting_a_password_needs_the_login_to_exist()
    {
        var (client, _) = NewClient();
        await client.CreateAsync("alice", "deploy", Password, CancellationToken.None);

        var known = await client.SetPasswordAsync("alice", "deploy", Password, CancellationToken.None);
        var unknown = await client.SetPasswordAsync("alice", "ghost", Password, CancellationToken.None);

        Assert.True(known.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, unknown.Error!.Code);
    }

    /// <summary>Deleting a login removes it, and a second delete answers not found.</summary>
    [Fact]
    public async Task Deleting_a_login_removes_it_and_a_second_delete_answers_not_found()
    {
        var (client, ledger) = NewClient();
        await client.CreateAsync("alice", "deploy", Password, CancellationToken.None);

        var deleted = await client.DeleteAsync("alice", "deploy", CancellationToken.None);
        var again = await client.DeleteAsync("alice", "deploy", CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, again.Error!.Code);
        Assert.Null(ledger.Logins.Find("alice", "deploy"));
    }

    /// <summary>Locking an account's logins ends the sessions of both daemons' logins and reports the count.</summary>
    [Fact]
    public async Task Locking_an_account_ends_the_sessions_of_both_daemons_logins_and_reports_the_count()
    {
        var (client, ledger) = NewClient();
        ledger.Logins.Create("alice", "deploy", 2);
        ledger.FtpsLogins.CreateLogin("alice", "uploads", 3);

        var locked = await client.SetAccountLoginsLockedAsync("alice", true, CancellationToken.None);

        Assert.Equal(5u, locked.Value.SessionsEnded);
        Assert.Equal(0, ledger.Logins.Find("alice", "deploy")!.OpenSessions);
    }

    /// <summary>Locking an account leaves another account's sessions open.</summary>
    [Fact]
    public async Task Locking_an_account_leaves_another_accounts_sessions_open()
    {
        var (client, ledger) = NewClient();
        ledger.Logins.Create("alice", "deploy", 2);
        ledger.Logins.Create("bob", "deploy", 4);

        await client.SetAccountLoginsLockedAsync("alice", true, CancellationToken.None);

        Assert.Equal(4, ledger.Logins.Find("bob", "deploy")!.OpenSessions);
    }

    /// <summary>
    /// Locking an account with no login succeeds and ended zero sessions, which is a count and not an absence.
    /// </summary>
    [Fact]
    public async Task Locking_an_account_with_no_login_succeeds_having_ended_zero_sessions()
    {
        var (client, _) = NewClient();

        var locked = await client.SetAccountLoginsLockedAsync("nobody", true, CancellationToken.None);

        Assert.True(locked.IsSuccess);
        Assert.Equal(0u, locked.Value.SessionsEnded);
    }

    /// <summary>Unlocking states no count, because it ends no session, and leaves the logins in place.</summary>
    [Fact]
    public async Task Unlocking_states_no_count_and_leaves_the_logins_in_place()
    {
        var (client, ledger) = NewClient();
        ledger.Logins.Create("alice", "deploy", 0);

        var unlocked = await client.SetAccountLoginsLockedAsync("alice", false, CancellationToken.None);

        Assert.True(unlocked.IsSuccess);
        Assert.Null(unlocked.Value.SessionsEnded);
        Assert.NotNull(ledger.Logins.Find("alice", "deploy"));
    }

    /// <summary>Ending a login's sessions reports two processes per session and a second call finds none.</summary>
    [Fact]
    public async Task Ending_a_logins_sessions_reports_two_processes_per_session_and_a_second_call_finds_none()
    {
        var (client, ledger) = NewClient();
        ledger.Logins.Create("alice", "deploy", 2);

        var first = await client.EndSessionsAsync("alice", "deploy", CancellationToken.None);
        var second = await client.EndSessionsAsync("alice", "deploy", CancellationToken.None);

        Assert.Equal(4, first.Value);
        Assert.Equal(0, second.Value);
    }

    /// <summary>Ending the sessions of a login that does not exist answers not found.</summary>
    [Fact]
    public async Task Ending_the_sessions_of_an_unknown_login_answers_not_found()
    {
        var (client, _) = NewClient();

        var ended = await client.EndSessionsAsync("alice", "ghost", CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotFoundCode, ended.Error!.Code);
    }

    /// <summary>Setting keys replaces the whole set, and an empty list clears every key.</summary>
    [Fact]
    public async Task Setting_keys_replaces_the_whole_set_and_an_empty_list_clears_it()
    {
        var (client, ledger) = NewClient();
        await client.CreateAsync("alice", "deploy", Password, CancellationToken.None);
        await client.SetAuthorizedKeysAsync("alice", "deploy", [KeyLine, KeyLine + "2"], CancellationToken.None);

        await client.SetAuthorizedKeysAsync("alice", "deploy", ["ssh-ed25519 only"], CancellationToken.None);
        Assert.Equal(SingleKey, ledger.Logins.Find("alice", "deploy")!.PublicKeys);

        var cleared = await client.SetAuthorizedKeysAsync("alice", "deploy", [], CancellationToken.None);
        Assert.True(cleared.IsSuccess);
        Assert.Empty(ledger.Logins.Find("alice", "deploy")!.PublicKeys);
    }

    /// <summary>Setting keys on a login that does not exist answers not found.</summary>
    [Fact]
    public async Task Setting_keys_on_an_unknown_login_answers_not_found()
    {
        var (client, _) = NewClient();

        var set = await client.SetAuthorizedKeysAsync("alice", "ghost", [KeyLine], CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotFoundCode, set.Error!.Code);
    }

    /// <summary>A refused key set is refused whole: the keys the login held stay.</summary>
    [Fact]
    public async Task A_refused_key_set_is_refused_whole_and_the_keys_the_login_held_stay()
    {
        var (client, ledger) = NewClient();
        await client.CreateAsync("alice", "deploy", Password, CancellationToken.None);
        await client.SetAuthorizedKeysAsync("alice", "deploy", [KeyLine], CancellationToken.None);
        var tooMany = Enumerable.Range(0, SimulatedAgentLimits.Default.MaxPublicKeysPerLogin + 1)
            .Select(index => { return $"{KeyLine}{index}"; })
            .ToList();
        var tooLong = new string('k', SimulatedAgentLimits.Default.MaxPublicKeyLength + 1);

        var many = await client.SetAuthorizedKeysAsync("alice", "deploy", tooMany, CancellationToken.None);
        var oversized = await client.SetAuthorizedKeysAsync(
            "alice",
            "deploy",
            [KeyLine, tooLong],
            CancellationToken.None);
        var blank = await client.SetAuthorizedKeysAsync("alice", "deploy", [KeyLine, " "], CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, many.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, oversized.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, blank.Error!.Code);
        Assert.Equal(InstalledKeys, ledger.Logins.Find("alice", "deploy")!.PublicKeys);
    }

    /// <summary>Builds a client over a fresh ledger and returns both.</summary>
    /// <returns>The client and the ledger it reads, so a test can seed and inspect the state.</returns>
    private static (SimulatedAgentSftpClient Client, SimulatedAgentLedger Ledger) NewClient()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);

        return (new SimulatedAgentSftpClient(ledger), ledger);
    }
}
