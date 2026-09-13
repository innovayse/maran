using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Domain.Policies;
using Maran.Modules.Ftp.Queries.GetFtpsStatus;
using Maran.Modules.Ftp.Tests.TestSupport;

namespace Maran.Modules.Ftp.Tests.Queries.GetFtpsStatus;

/// <summary>Behaviour of the query that reports what this server's FTPS daemon is doing.</summary>
public sealed class GetFtpsStatusQueryHandlerTests
{
    /// <summary>The hostname FTPS is enabled for in these tests.</summary>
    private const string FtpsHostname = "ftp.example.test";

    /// <summary>A daemon that no longer forces tls is reported as not forcing it.</summary>
    /// <remarks>
    /// The defect this field exists to prevent, asserted on the screen's own shape: every other value
    /// here is healthy — the unit is active, the control port greets, real certificate material is
    /// present, the panel asked for TLS — and the answer must still say TLS is not enforced. A
    /// handler that filled this from the panel's intention would report green on a daemon taking
    /// passwords in the clear.
    /// </remarks>
    [Fact]
    public async Task A_daemon_that_no_longer_forces_tls_is_reported_as_not_forcing_it()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(FtpsHostname);
        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(FtpsHostname), CancellationToken.None);
        context.Agent.NextStatus = context.Agent.NextStatus with { ForcedTls = false };

        var result = await context.StatusHandler.HandleAsync(new GetFtpsStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ForcedTls);
        Assert.True(result.Value.Running);
        Assert.True(result.Value.ControlPortAnswered);
        Assert.True(result.Value.CertificatePresent);
        Assert.True(result.Value.Enabled);
    }

    /// <summary>A server where ftps was never enabled reports the absence as an absence.</summary>
    /// <remarks>
    /// No hostname is persisted, so the agent is asked about none and the three certificate fields
    /// come back absent-valued because nothing was asked — not because nothing exists. The screen
    /// gets a null hostname beside them and must render that as absence rather than composing a host
    /// name of its own: a customer told to connect to a name the certificate was not issued for gets
    /// a name-mismatch warning they cannot tell from an attack.
    /// </remarks>
    [Fact]
    public async Task A_server_where_ftps_was_never_enabled_reports_the_absence_as_an_absence()
    {
        using var context = new FtpTestContext();

        var result = await context.StatusHandler.HandleAsync(new GetFtpsStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Hostname);
        Assert.False(result.Value.Enabled);
        Assert.Equal(string.Empty, context.Agent.LastStatusHostname);
    }

    /// <summary>The status names the hostname the panel persisted, which the wire never returns.</summary>
    /// <remarks>
    /// The contract carries the hostname as a REQUEST field only, so nothing but this module's own
    /// row can supply it — and the credential dialog a customer is shown is built on it.
    /// </remarks>
    [Fact]
    public async Task The_status_names_the_hostname_the_panel_persisted()
    {
        using var context = new FtpTestContext();
        context.Sites.Serve(FtpsHostname);
        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(FtpsHostname), CancellationToken.None);

        var result = await context.StatusHandler.HandleAsync(new GetFtpsStatusQuery(), CancellationToken.None);

        Assert.Equal(FtpsHostname, result.Value.Hostname);
        Assert.Equal(FtpsHostname, context.Agent.LastStatusHostname);
    }

    /// <summary>The control port comes from FtpsDefaults and not from the host.</summary>
    /// <remarks>
    /// The panel is what tells an operator which port to open in their firewall, so the number they
    /// are told has to be the number the daemon was configured with — one constant, read by both.
    /// </remarks>
    [Fact]
    public async Task The_control_port_comes_from_FtpsDefaults()
    {
        using var context = new FtpTestContext();

        var result = await context.StatusHandler.HandleAsync(new GetFtpsStatusQuery(), CancellationToken.None);

        Assert.Equal(FtpsDefaults.ControlPort, result.Value.ControlPort);
    }
}
