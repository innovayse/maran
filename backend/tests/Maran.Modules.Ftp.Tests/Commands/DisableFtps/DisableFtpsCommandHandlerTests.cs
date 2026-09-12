using Maran.Modules.Ftp.Commands.DisableFtps;
using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Services;
using Maran.Modules.Ftp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ftp.Tests.Commands.DisableFtps;

/// <summary>Behaviour of the handler that stops this server's FTPS daemon.</summary>
public sealed class DisableFtpsCommandHandlerTests
{
    /// <summary>The hostname FTPS is enabled for in these tests.</summary>
    private const string FtpsHostname = "ftp.example.test";

    /// <summary>Disabling ftps stops the daemon and records the panel no longer wants it.</summary>
    [Fact]
    public async Task Disabling_ftps_stops_the_daemon_and_records_that_the_panel_no_longer_wants_it()
    {
        using var context = await EnabledContextAsync();

        await context.DisableHandler.HandleAsync(new DisableFtpsCommand(), CancellationToken.None);

        Assert.Equal(1, context.Agent.DisableCalls);
        var row = await context.Database.FtpsSettings.SingleAsync(CancellationToken.None);
        Assert.False(row.Enabled);
    }

    /// <summary>Disabling ftps keeps the hostname and the range on the row.</summary>
    /// <remarks>
    /// Disabling a service is not forgetting how it was configured. An operator who switches FTPS
    /// back on expects the same hostname and the same ports, and re-typing them is how the panel and
    /// the firewall stop agreeing about the passive range.
    /// </remarks>
    [Fact]
    public async Task Disabling_ftps_keeps_the_hostname_and_the_range()
    {
        using var context = await EnabledContextAsync();

        await context.DisableHandler.HandleAsync(new DisableFtpsCommand(), CancellationToken.None);

        var row = await context.Database.FtpsSettings.SingleAsync(CancellationToken.None);
        Assert.Equal(FtpsHostname, row.Hostname);
        Assert.Equal(30000, row.PassivePortMin);
        Assert.Equal(30099, row.PassivePortMax);
    }

    /// <summary>Disabling ftps on a server that never enabled it is not refused.</summary>
    /// <remarks>
    /// The agent's disable is idempotent, so a server whose daemon is already down converges rather
    /// than fails — and a panel that refused would leave an operator unable to switch off a daemon
    /// the panel had lost track of, which is precisely the state they most want to be able to end.
    /// </remarks>
    [Fact]
    public async Task Disabling_ftps_on_a_server_that_never_enabled_it_is_not_refused()
    {
        using var context = new FtpTestContext();

        var result = await context.DisableHandler.HandleAsync(
            new DisableFtpsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, context.Agent.DisableCalls);
    }

    /// <summary>Disabling ftps records an audit entry naming the hostname.</summary>
    [Fact]
    public async Task Disabling_ftps_records_an_audit_entry_naming_the_hostname()
    {
        using var context = await EnabledContextAsync();
        var before = context.Entries.Count;

        await context.DisableHandler.HandleAsync(new DisableFtpsCommand(), CancellationToken.None);

        var entry = context.Entries[before];
        Assert.Equal(FtpAuditJournal.FtpsDisabled, entry.Action);
        Assert.Equal(FtpsHostname, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Builds a context with FTPS already enabled for <see cref="FtpsHostname"/>.</summary>
    /// <returns>The assembled module with one enabled settings row.</returns>
    private static async Task<FtpTestContext> EnabledContextAsync()
    {
        var context = new FtpTestContext();
        context.Sites.Serve(FtpsHostname);

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(FtpsHostname), CancellationToken.None);

        return context;
    }
}
