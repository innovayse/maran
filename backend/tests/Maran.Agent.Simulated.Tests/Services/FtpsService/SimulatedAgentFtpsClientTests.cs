using Maran.Agent.Client.Services.FtpsService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.FtpsService;
using Maran.Agent.Simulated.State;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Simulated.Tests.Services.FtpsService;

/// <summary>The simulated agent's FTPS operations answer like the real agent's.</summary>
public sealed class SimulatedAgentFtpsClientTests
{
    /// <summary>The host name the tests enable the daemon for.</summary>
    private const string Hostname = "panel.example.com";

    /// <summary>A password the tests hand over; the simulation accepts it and keeps nothing of it.</summary>
    private static readonly SensitiveString Password = new("correct horse battery staple");

    /// <summary>
    /// Enabling the daemon reports it running, serving the host name's certificate and the requested range.
    /// </summary>
    [Fact]
    public async Task Enabling_the_daemon_reports_it_running_with_the_requested_range_and_a_self_signed_certificate()
    {
        var client = NewClient();

        var enabled = await client.EnableAsync(Hostname, 40000, 40100, string.Empty, 50, CancellationToken.None);

        Assert.True(enabled.IsSuccess);
        Assert.True(enabled.Value.Running);
        Assert.True(enabled.Value.ControlPortAnswered);
        Assert.Equal(FtpsCertificate.SelfSigned, enabled.Value.Certificate);
        Assert.Equal(
            $"{SimulatedFtpsStore.CertificateDirectory}{Hostname}/fullchain.pem",
            enabled.Value.CertificatePath);
        Assert.Equal(40000u, enabled.Value.PassivePortMin);
        Assert.Equal(40100u, enabled.Value.PassivePortMax);
        Assert.Equal(FtpsTlsEnforcement.Enforced, enabled.Value.TlsEnforcement);
    }

    /// <summary>Enabling twice with the same values gives the same answer.</summary>
    [Fact]
    public async Task Enabling_twice_with_the_same_values_gives_the_same_answer()
    {
        var client = NewClient();

        var first = await client.EnableAsync(Hostname, 40000, 40100, string.Empty, 50, CancellationToken.None);
        var second = await client.EnableAsync(Hostname, 40000, 40100, string.Empty, 50, CancellationToken.None);

        Assert.Equal(first.Value, second.Value);
    }

    /// <summary>
    /// A host name, range or ceiling the agent refuses is invalid input and leaves the daemon as it was.
    /// </summary>
    [Theory]
    [InlineData("not a host name", 40000u, 40100u, 50u)]
    [InlineData("", 40000u, 40100u, 50u)]
    [InlineData(Hostname, 0u, 40100u, 50u)]
    [InlineData(Hostname, 40100u, 40000u, 50u)]
    [InlineData(Hostname, 40000u, 65536u, 50u)]
    [InlineData(Hostname, 40000u, 40100u, 0u)]
    public async Task Enabling_with_values_the_agent_refuses_is_invalid_input_and_changes_nothing(
        string hostname,
        uint portMin,
        uint portMax,
        uint maxClients)
    {
        var client = NewClient();

        var enabled = await client.EnableAsync(
            hostname,
            portMin,
            portMax,
            string.Empty,
            maxClients,
            CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, enabled.Error!.Code);
        Assert.False((await client.GetStatusAsync(string.Empty, CancellationToken.None)).Value.Running);
    }

    /// <summary>
    /// A daemon never enabled is stopped, has no range, and with no host name asked about reports no certificate.
    /// </summary>
    [Fact]
    public async Task A_daemon_never_enabled_is_stopped_with_no_range_and_no_certificate_asked_about()
    {
        var client = NewClient();

        var status = await client.GetStatusAsync(string.Empty, CancellationToken.None);

        Assert.False(status.Value.Running);
        Assert.False(status.Value.ControlPortAnswered);
        Assert.Equal(FtpsCertificate.Absent, status.Value.Certificate);
        Assert.Equal(string.Empty, status.Value.CertificatePath);
        Assert.Equal(0u, status.Value.PassivePortMin);
        Assert.Equal(0u, status.Value.PassivePortMax);
        Assert.Equal(FtpsTlsEnforcement.NotObserved, status.Value.TlsEnforcement);
    }

    /// <summary>Asking about a host name reports its certificate material even while the daemon is stopped.</summary>
    [Fact]
    public async Task Asking_about_a_host_name_reports_its_certificate_while_the_daemon_is_stopped()
    {
        var client = NewClient();

        var status = await client.GetStatusAsync(Hostname, CancellationToken.None);

        Assert.False(status.Value.Running);
        Assert.Equal(FtpsCertificate.SelfSigned, status.Value.Certificate);
    }

    /// <summary>Disabling stops the daemon, keeps its range, and keeps every login.</summary>
    [Fact]
    public async Task Disabling_stops_the_daemon_and_keeps_its_range_and_every_login()
    {
        var client = NewClient();
        await client.EnableAsync(Hostname, 40000, 40100, string.Empty, 50, CancellationToken.None);
        await client.CreateUserAsync(
            new CreateFtpsUserArguments("alice", "uploads", Password),
            CancellationToken.None);

        var disabled = await client.DisableAsync(CancellationToken.None);

        Assert.False(disabled.Value.Running);
        Assert.False(disabled.Value.ControlPortAnswered);
        Assert.Equal(40000u, disabled.Value.PassivePortMin);
        Assert.True((await client.SetPasswordAsync("alice", "uploads", Password, CancellationToken.None)).IsSuccess);
    }

    /// <summary>Reloading the certificate leaves a stopped daemon stopped.</summary>
    [Fact]
    public async Task Reloading_the_certificate_leaves_a_stopped_daemon_stopped()
    {
        var client = NewClient();
        await client.EnableAsync(Hostname, 40000, 40100, string.Empty, 50, CancellationToken.None);
        await client.DisableAsync(CancellationToken.None);

        var reloaded = await client.ReloadTlsAsync(CancellationToken.None);

        Assert.True(reloaded.IsSuccess);
        Assert.False(reloaded.Value.Running);
    }

    /// <summary>Reloading the certificate of a running daemon reports it still running.</summary>
    [Fact]
    public async Task Reloading_the_certificate_of_a_running_daemon_reports_it_still_running()
    {
        var client = NewClient();
        await client.EnableAsync(Hostname, 40000, 40100, string.Empty, 50, CancellationToken.None);

        var reloaded = await client.ReloadTlsAsync(CancellationToken.None);

        Assert.True(reloaded.Value.Running);
        Assert.Equal(FtpsCertificate.SelfSigned, reloaded.Value.Certificate);
    }

    /// <summary>Creating a login names it under the account, and creating it again answers already exists.</summary>
    [Fact]
    public async Task Creating_a_login_names_it_under_the_account_and_creating_it_again_answers_already_exists()
    {
        var client = NewClient();
        var arguments = new CreateFtpsUserArguments("alice", "uploads", Password);

        var created = await client.CreateUserAsync(arguments, CancellationToken.None);
        var again = await client.CreateUserAsync(arguments, CancellationToken.None);

        Assert.Equal("alice_uploads", created.Value);
        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, again.Error!.Code);
    }

    /// <summary>A login shares no namespace with the same suffix on the SFTP side.</summary>
    [Fact]
    public async Task A_login_shares_no_namespace_with_the_same_suffix_on_the_sftp_side()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);
        var client = new SimulatedAgentFtpsClient(ledger);
        ledger.Logins.Create("alice", "uploads", 0);

        var created = await client.CreateUserAsync(
            new CreateFtpsUserArguments("alice", "uploads", Password),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
    }

    /// <summary>Once the server holds as many logins as it will, creating another answers limit reached.</summary>
    [Fact]
    public async Task Creating_a_login_beyond_the_ceiling_answers_limit_reached()
    {
        var client = NewClient();
        for (var index = 0; index < SimulatedAgentLimits.Default.MaxFtpsLogins; index++)
        {
            var arguments = new CreateFtpsUserArguments("alice", $"l{index}", Password);
            Assert.True((await client.CreateUserAsync(arguments, CancellationToken.None)).IsSuccess);
        }

        var over = await client.CreateUserAsync(
            new CreateFtpsUserArguments("alice", "one-too-many", Password),
            CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over.Error!.Code);
    }

    /// <summary>Setting a password needs the login to exist and creates none.</summary>
    [Fact]
    public async Task Setting_a_password_needs_the_login_to_exist()
    {
        var client = NewClient();
        await client.CreateUserAsync(new CreateFtpsUserArguments("alice", "uploads", Password), CancellationToken.None);

        var known = await client.SetPasswordAsync("alice", "uploads", Password, CancellationToken.None);
        var unknown = await client.SetPasswordAsync("alice", "ghost", Password, CancellationToken.None);

        Assert.True(known.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, unknown.Error!.Code);
    }

    /// <summary>Deleting a login removes it, and a second delete answers not found.</summary>
    [Fact]
    public async Task Deleting_a_login_removes_it_and_a_second_delete_answers_not_found()
    {
        var client = NewClient();
        await client.CreateUserAsync(new CreateFtpsUserArguments("alice", "uploads", Password), CancellationToken.None);

        var deleted = await client.DeleteUserAsync("alice", "uploads", CancellationToken.None);
        var again = await client.DeleteUserAsync("alice", "uploads", CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, again.Error!.Code);
    }

    /// <summary>Builds a client over a fresh ledger.</summary>
    /// <returns>The client.</returns>
    private static SimulatedAgentFtpsClient NewClient()
    {
        return new SimulatedAgentFtpsClient(new SimulatedAgentLedger(SimulatedAgentLimits.Default));
    }
}
