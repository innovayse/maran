using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.IntegrationEvents.Handlers;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.SharedKernel.Apps;

namespace Maran.Modules.Sites.Tests.IntegrationEvents.Handlers;

/// <summary>What the Sites module hands the agent when an account is deleted: every vhost, for the installed web server.</summary>
public sealed class AccountDeletionCascadeTests
{
    /// <summary>The system user name the event carries.</summary>
    private const string Username = "acme";

    /// <summary>Every vhost of the account is removed from the installed web server.</summary>
    [Theory]
    [InlineData(StackWebServer.Nginx, SiteWebServer.Nginx)]
    [InlineData(StackWebServer.Apache, SiteWebServer.Apache)]
    public async Task Every_vhost_of_the_account_is_removed_from_the_installed_web_server(StackWebServer installed, SiteWebServer expected)
    {
        // An unset web server reads as nginx on the wire, so deleting an Apache host's account without this
        // would leave its vhosts behind pointing at a home directory that no longer exists.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(account, "a.example.com"));
            seed.Sites.Add(SitesTestContext.PhpSite(account, "b.example.com"));
            await seed.SaveChangesAsync();
        }

        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountDeletingHandler(context, agent, SiteStacks.With(installed, ["8.3"], [])).HandleAsync(
            new AccountDeleting(account, Username), CancellationToken.None);

        Assert.Equal([expected, expected], agent.DeleteWebServers);
    }

    /// <summary>With sites to remove and no installed web server the deletion fails and removes nothing.</summary>
    [Fact]
    public async Task With_sites_and_no_installed_web_server_the_deletion_fails_and_removes_nothing()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(account, "a.example.com"));
            await seed.SaveChangesAsync();
        }

        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return new AccountDeletingHandler(context, agent, SiteStacks.NotReady()).HandleAsync(
                new AccountDeleting(account, Username), CancellationToken.None);
        });
        Assert.Empty(agent.DeleteWebServers);
    }

    /// <summary>An account with no sites is deleted on a host with no web server (the inverse control).</summary>
    [Fact]
    public async Task An_account_with_no_sites_is_deleted_on_a_host_with_no_web_server()
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());

        await new AccountDeletingHandler(context, new RecordingAgentSitesClient(), SiteStacks.NotReady()).HandleAsync(
            new AccountDeleting(Guid.NewGuid(), Username), CancellationToken.None);
    }
}
