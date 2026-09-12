using Maran.Modules.Ftp.Commands.DisableFtps;
using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Services;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ftp.Tests.Services;

/// <summary>
/// Behaviour of the reloader the Ssl module's certificate installation drives.
/// </summary>
public sealed class FtpsTlsReloaderTests
{
    /// <summary>The hostname FTPS is enabled for in these tests.</summary>
    private const string FtpsHostname = "ftp.example.test";

    /// <summary>A certificate installed for the ftps hostname restarts the daemon.</summary>
    [Fact]
    public async Task A_certificate_installed_for_the_ftps_hostname_restarts_the_daemon()
    {
        using var context = await EnabledContextAsync();

        await context.Reloader.ReloadForAsync(FtpsHostname, CancellationToken.None);

        Assert.Equal(1, context.Agent.ReloadTlsCalls);
    }

    /// <summary>A certificate installed for any other domain does not touch the daemon.</summary>
    /// <remarks>
    /// The control that stops this becoming "restart vsftpd on every renewal on the box", which on a
    /// busy server is a restart a day and every transfer in flight aborted with it.
    /// </remarks>
    [Fact]
    public async Task A_certificate_installed_for_any_other_domain_does_not_touch_the_daemon()
    {
        using var context = await EnabledContextAsync();

        await context.Reloader.ReloadForAsync("shop.example.test", CancellationToken.None);

        Assert.Equal(0, context.Agent.ReloadTlsCalls);
    }

    /// <summary>A certificate installed while ftps is disabled does not start the daemon.</summary>
    /// <remarks>
    /// A restart is not a start, but the agent's reload leaves a stopped daemon alone — so the
    /// protection that matters here is not calling it at all: an operator who switched FTPS off
    /// must not have it woken by somebody else's renewal.
    /// </remarks>
    [Fact]
    public async Task A_certificate_installed_while_ftps_is_disabled_does_not_touch_the_daemon()
    {
        using var context = await EnabledContextAsync();
        await context.DisableHandler.HandleAsync(new DisableFtpsCommand(), CancellationToken.None);

        await context.Reloader.ReloadForAsync(FtpsHostname, CancellationToken.None);

        Assert.Equal(0, context.Agent.ReloadTlsCalls);
    }

    /// <summary>A certificate installed on a server where ftps was never enabled does nothing.</summary>
    /// <remarks>
    /// There is no settings row at all, so the comparison has nothing to compare against. It must
    /// answer "not mine" rather than throw: the Ssl module's installation succeeded, and a
    /// notification that faulted would report that success as a failure.
    /// </remarks>
    [Fact]
    public async Task A_certificate_installed_where_ftps_was_never_enabled_does_nothing()
    {
        using var context = new FtpTestContext();

        var reloaded = await context.Reloader.ReloadForAsync(FtpsHostname, CancellationToken.None);

        Assert.False(reloaded);
        Assert.Equal(0, context.Agent.ReloadTlsCalls);
        Assert.Empty(context.Entries);
    }

    /// <summary>The domain comparison ignores case, because dns does.</summary>
    /// <remarks>
    /// A certificate installed for <c>FTP.Example.Test</c> is material for the same daemon as one
    /// installed for <c>ftp.example.test</c>. An ordinal comparison would leave the daemon serving
    /// the superseded certificate with nothing on any screen to say why.
    /// </remarks>
    [Fact]
    public async Task The_domain_comparison_ignores_case_because_dns_does()
    {
        using var context = await EnabledContextAsync();

        await context.Reloader.ReloadForAsync("FTP.Example.Test", CancellationToken.None);

        Assert.Equal(1, context.Agent.ReloadTlsCalls);
    }

    /// <summary>A successful reload is journalled as unattended work with no caller.</summary>
    /// <remarks>
    /// The actor is the panel itself: this happens because the Ssl module replaced material, possibly
    /// on a renewal timer at four in the morning, so the address and client columns stay empty rather
    /// than carrying the actor's name.
    /// </remarks>
    [Fact]
    public async Task A_successful_reload_is_journalled_as_unattended_work()
    {
        using var context = await EnabledContextAsync();
        var before = context.Entries.Count;

        await context.Reloader.ReloadForAsync(FtpsHostname, CancellationToken.None);

        var entry = context.Entries[before];
        Assert.Equal(FtpAuditJournal.FtpsTlsReloaded, entry.Action);
        Assert.Equal(FtpsHostname, entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Null(entry.ActorUserId);
        Assert.Equal("maran-ftp", entry.ActorUsername);
        Assert.Equal(string.Empty, entry.IpAddress);
    }

    /// <summary>A refused reload is journalled as a failure naming the hostname.</summary>
    /// <remarks>
    /// This is the whole reason the reloader exists as a named unit. When the agent refuses, the
    /// daemon is still running, still answering and still presenting the OLD certificate — a state in
    /// which every other status field reads healthy — and the operator who installed the certificate
    /// did not ask for this and is not watching it. An event handled silently that did nothing is
    /// worse than one that refused, so the refusal is a row an operator can find.
    /// </remarks>
    [Fact]
    public async Task A_refused_reload_is_journalled_as_a_failure_naming_the_hostname()
    {
        using var context = await EnabledContextAsync();
        context.Agent.NextReloadError = Error.Of("AgentSystemFailure", ErrorType.Failure);
        var before = context.Entries.Count;

        var reloaded = await context.Reloader.ReloadForAsync(FtpsHostname, CancellationToken.None);

        Assert.False(reloaded);
        var entry = context.Entries[before];
        Assert.Equal(FtpAuditJournal.FtpsTlsReloaded, entry.Action);
        Assert.Equal(FtpsHostname, entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Builds a context with FTPS already enabled for <see cref="FtpsHostname"/>.</summary>
    /// <returns>The assembled module, with one settings row and the agent's counters at their enable-path values.</returns>
    private static async Task<FtpTestContext> EnabledContextAsync()
    {
        var context = new FtpTestContext();
        context.Sites.Serve(FtpsHostname);

        await context.EnableHandler.HandleAsync(
            new EnableFtpsCommand(FtpsHostname), CancellationToken.None);

        return context;
    }
}
