using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The installer holds the site's operation lock for the whole install, so a certificate never rewrites
/// a vhost while another operation (a PHP version change, a suspension) is rewriting the same site.
/// </summary>
/// <remarks>
/// Every refusing test here has an accepting control beside it: a lock that refused everything, or one
/// that was never released, would satisfy the refusals on their own.
/// </remarks>
public sealed class CertificateInstallerLockTests
{
    /// <summary>The domain every test here installs for.</summary>
    private const string Domain = "example.com";

    /// <summary>The longest any waiting step may take before the test calls it a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    /// <summary>Installs for the fixture's site with the given wait.</summary>
    /// <param name="fixture">The fixture supplying the installer.</param>
    /// <param name="site">The site to install for.</param>
    /// <param name="wait">How long to wait for the site's lock.</param>
    /// <returns>The installer's answer.</returns>
    private static Task<Result<DateTimeOffset>> InstallAsync(SslHandlerFixture fixture, SiteSnapshot site, TimeSpan wait)
    {
        return fixture.Installer.InstallAsync(fixture.Username, site, "cert", "key", wait, CancellationToken.None);
    }

    /// <summary>A site held by another operation is refused as busy and the agent is never called.</summary>
    [Fact]
    public async Task A_site_held_by_another_operation_is_refused_as_busy_and_the_agent_is_never_called()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var site = fixture.SiteFor(Domain);
        await using var held = (await fixture.SiteLocks.AcquireAsync(site.Id, TimeSpan.Zero, CancellationToken.None)).Value;

        var result = await InstallAsync(fixture, site, TimeSpan.Zero);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteBusy", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(fixture.Agent.Installs);
        Assert.Empty(fixture.Sites.Attached);
    }

    /// <summary>The accounting control: a free site installs, and is free again afterwards.</summary>
    [Fact]
    public async Task A_free_site_installs_and_is_free_again_afterwards()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var site = fixture.SiteFor(Domain);

        var result = await InstallAsync(fixture, site, TimeSpan.Zero);

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Agent.Installs);
        Assert.Equal([site.Id], fixture.Sites.Attached);
        var again = await fixture.SiteLocks.AcquireAsync(site.Id, TimeSpan.Zero, CancellationToken.None);
        Assert.True(again.IsSuccess);
    }

    /// <summary>A refused install still releases the site, so one failure does not wedge every later operation.</summary>
    [Fact]
    public async Task A_refused_install_still_releases_the_site()
    {
        using var fixture = new SslHandlerFixture(
            [Domain], agentFailure: Error.Of("AgentSystemFailure", ErrorType.Failure));
        var site = fixture.SiteFor(Domain);

        var result = await InstallAsync(fixture, site, TimeSpan.Zero);

        Assert.False(result.IsSuccess);
        Assert.True((await fixture.SiteLocks.AcquireAsync(site.Id, TimeSpan.Zero, CancellationToken.None)).IsSuccess);
    }

    /// <summary>
    /// The barrier: an install that arrives while the site is held does not touch the host until the
    /// holder lets go, and then runs exactly once.
    /// </summary>
    [Fact]
    public async Task An_install_waits_for_the_holder_and_runs_only_after_it_lets_go()
    {
        var locks = new ParkingSiteLock(new SiteOperationLock());
        using var fixture = new SslHandlerFixture([Domain], siteLock: locks);
        var site = fixture.SiteFor(Domain);
        var held = (await fixture.SiteLocks.AcquireAsync(site.Id, TimeSpan.Zero, CancellationToken.None)).Value;

        var install = InstallAsync(fixture, site, Hang);

        // Parked inside the lock is an event, not a pause: a lock that let the install through never parks
        // it, and this wait then times out instead of the test passing on a slow machine.
        await locks.Parked.WaitAsync(Hang);

        Assert.False(install.IsCompleted);
        Assert.Empty(fixture.Agent.Installs);

        await held.DisposeAsync();
        var result = await install.WaitAsync(Hang);

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Agent.Installs);
    }

    /// <summary>An install for a different site is not held up by the busy one.</summary>
    [Fact]
    public async Task An_install_for_a_different_site_is_not_held_up_by_a_busy_one()
    {
        using var fixture = new SslHandlerFixture(["busy.example.com", "free.example.com"]);
        await using var held = (await fixture.SiteLocks.AcquireAsync(
            fixture.SiteFor("busy.example.com").Id, TimeSpan.Zero, CancellationToken.None)).Value;

        var result = await InstallAsync(fixture, fixture.SiteFor("free.example.com"), TimeSpan.Zero);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A site deleted while the install waited is answered not found and the host is left alone.</summary>
    [Fact]
    public async Task A_site_that_disappeared_while_waiting_is_answered_not_found_and_the_host_is_left_alone()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var vanished = fixture.SiteFor(Domain) with { Id = Guid.NewGuid() };

        var result = await InstallAsync(fixture, vanished, TimeSpan.Zero);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Empty(fixture.Agent.Installs);
        Assert.Empty(fixture.Sites.Attached);
    }

    /// <summary>The vhost is rendered from the site as it is once the lock is held, not as the caller saw it.</summary>
    [Fact]
    public async Task The_vhost_is_rendered_from_the_site_as_it_is_once_the_lock_is_held()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        var stale = fixture.SiteFor(Domain) with { PhpVersion = "7.4" };

        await InstallAsync(fixture, stale, TimeSpan.Zero);

        Assert.Equal("8.3", Assert.Single(fixture.Agent.Installs).Site.PhpVersion);
    }

    /// <summary>
    /// The descriptor sent to the agent is the site's own, aliases included, because the host recognises a
    /// stub (a disabled site, a suspended account) by comparing its vhost with the stub rendered for it.
    /// </summary>
    [Fact]
    public async Task The_descriptor_sent_to_the_agent_is_the_sites_own_so_the_host_can_recognise_a_stub()
    {
        using var fixture = new SslHandlerFixture([Domain]);

        await InstallAsync(fixture, fixture.SiteFor(Domain), TimeSpan.Zero);

        var sent = Assert.Single(fixture.Agent.Installs).Site;
        Assert.Equal(["www." + Domain], sent.Aliases);
        Assert.True(sent.HasCertificate);
    }
}
