using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.ListPhpVersions;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Queries.ListPhpVersions;

/// <summary>Behavioral contract of <see cref="ListPhpVersionsQueryHandler"/>.</summary>
public sealed class ListPhpVersionsQueryHandlerTests
{
    /// <summary>Listing php versions reports what the host has installed.</summary>
    [Fact]
    public async Task Listing_php_versions_reports_what_the_host_has_installed()
    {
        var handler = Handler(new RecordingAgentPhpClient("8.3", "8.4"), FakeCurrentUser.Admin());

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var versions = result.Value.Select(version =>
        {
            return version.Version;
        });
        Assert.Equal(["8.3", "8.4"], versions);
    }

    /// <summary>Listing php versions never carries the fpm socket path outward.</summary>
    [Fact]
    public async Task Listing_php_versions_never_carries_the_fpm_socket_path_outward()
    {
        // The agent's own DTO has one; the module's does not, deliberately. A filesystem path is
        // operator-facing detail and must not reach a customer's screen (rules/security.md).
        var handler = Handler(new RecordingAgentPhpClient("8.3"), FakeCurrentUser.Admin());

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        var properties = result.Value[0].GetType().GetProperties().Select(property =>
        {
            return property.Name;
        });
        Assert.Equal(["Version", "DefaultStatus", "Lifecycle", "SitesUsing"], properties);
    }

    /// <summary>An unknown default stays unknown rather than becoming not-default.</summary>
    [Fact]
    public async Task An_unknown_default_stays_unknown_rather_than_becoming_not_default()
    {
        // Unknown and not-default are different answers: the agent does not currently establish the
        // host's default CLI PHP, and "not known" must not render as "not the default".
        var handler = Handler(new RecordingAgentPhpClient("8.3"), FakeCurrentUser.Admin());

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        Assert.Equal(PhpDefaultStatus.Unknown, result.Value[0].DefaultStatus);
    }

    /// <summary>An agent that cannot answer is a failure not an empty list.</summary>
    [Fact]
    public async Task An_agent_that_cannot_answer_is_a_failure_not_an_empty_list()
    {
        // An empty list would tell the SPA "this server has no PHP", which is a different and
        // wrong thing to show a customer whose agent is merely unreachable.
        var handler = Handler(new RecordingAgentPhpClient(Error.Of("AgentUnavailable", ErrorType.Unavailable)), FakeCurrentUser.Admin());

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }

    /// <summary>The version list carries each version's support life, read at the injected clock's day.</summary>
    [Fact]
    public async Task The_version_list_carries_each_versions_support_life_read_at_the_clocks_day()
    {
        var handler = Handler(new RecordingAgentPhpClient("7.4", "8.2", "8.4", "9.9"), FakeCurrentUser.Admin());

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        var statuses = result.Value.ToDictionary(
            version =>
            {
                return version.Version;
            },
            version =>
            {
                return version.Lifecycle.Status;
            });
        // 2026-11-07: 7.4 is long dead, 8.2 gets security fixes only, 8.4 is supported, 9.9 has no row.
        Assert.Equal(PhpSupportStatus.EndOfLife, statuses["7.4"]);
        Assert.Equal(PhpSupportStatus.SecurityOnly, statuses["8.2"]);
        Assert.Equal(PhpSupportStatus.Supported, statuses["8.4"]);
        Assert.Equal(PhpSupportStatus.Unknown, statuses["9.9"]);
        Assert.Equal("Security fixes only", result.Value.Single(version => { return version.Version == "8.2"; }).Lifecycle.StatusName);
        Assert.Equal(54, result.Value.Single(version => { return version.Version == "8.2"; }).Lifecycle.DaysLeft);
    }

    /// <summary>An administrator is told how many sites use each version, across every account.</summary>
    [Fact]
    public async Task An_administrator_is_told_how_many_sites_use_each_version_across_every_account()
    {
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "a.example.com", "8.3"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "b.example.com", "8.3"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "c.example.com", "8.2"));
            await seed.SaveChangesAsync();
        }

        var handler = Handler(new RecordingAgentPhpClient("8.2", "8.3", "8.4"), FakeCurrentUser.Admin(), database);

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        Assert.Equal(
            [1, 2, 0],
            result.Value.Select(version =>
            {
                return version.SitesUsing;
            }));
    }

    /// <summary>A customer is never told a site count: it would reveal how many other accounts share a version.</summary>
    [Fact]
    public async Task A_customer_is_never_told_a_site_count()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(SitesTestContext.PhpSite(mine, "mine.example.com", "8.3"));
            seed.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "theirs.example.com", "8.3"));
            await seed.SaveChangesAsync();
        }

        var handler = Handler(new RecordingAgentPhpClient("8.3"), FakeCurrentUser.Customer(mine), database);

        var result = await handler.HandleAsync(new ListPhpVersionsQuery(), CancellationToken.None);

        Assert.Null(result.Value[0].SitesUsing);
        // The lifecycle is still there: it is public knowledge about a version, not about other accounts.
        Assert.Equal(PhpSupportStatus.SecurityOnly, result.Value[0].Lifecycle.Status);
    }

    /// <summary>Builds the handler over the real display names, a fixed clock and the given caller.</summary>
    /// <param name="php">The agent's PHP surface.</param>
    /// <param name="caller">Who asks.</param>
    /// <param name="database">The in-memory database to read sites from; a fresh one when omitted.</param>
    /// <returns>The handler under test.</returns>
    private static ListPhpVersionsQueryHandler Handler(
        RecordingAgentPhpClient php,
        FakeCurrentUser caller,
        string? database = null)
    {
        return new ListPhpVersionsQueryHandler(
            php,
            new PhpVersionListing(
                SitesTestContext.Create(caller, database),
                new FakeClock(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero)),
                caller,
                SitesTestContext.SiteNames()));
    }
}
