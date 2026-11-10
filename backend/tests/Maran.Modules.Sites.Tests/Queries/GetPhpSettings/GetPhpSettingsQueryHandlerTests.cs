using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Queries.GetPhpSettings;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Sites.Tests.Queries.GetPhpSettings;

/// <summary>Behavioral contract of <see cref="GetPhpSettingsQueryHandler"/> with respect to the installed PHP apps.</summary>
public sealed class GetPhpSettingsQueryHandlerTests
{
    /// <summary>With no PHP installed as an app the settings report that, and the account is not even read.</summary>
    [Fact]
    public async Task With_no_php_installed_as_an_app_the_settings_report_it_is_not_installed()
    {
        var account = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), Guid.NewGuid().ToString());

        var result = await Handler(context, account, SiteStacks.WithoutRuntimes())
            .HandleAsync(new GetPhpSettingsQuery(account), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AppsNotInstalled", result.Error!.Code);
    }

    /// <summary>With PHP installed the same request is answered: the control for the refusal above.</summary>
    [Fact]
    public async Task With_php_installed_as_an_app_the_settings_are_answered()
    {
        var account = Guid.NewGuid();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), Guid.NewGuid().ToString());

        var result = await Handler(context, account, SiteStacks.Ready())
            .HandleAsync(new GetPhpSettingsQuery(account), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Builds the handler over the real display names and the given stack.</summary>
    /// <param name="context">The caller's tenant-scoped context.</param>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="stack">The installed stack.</param>
    private static GetPhpSettingsQueryHandler Handler(SitesDbContext context, Guid accountId, SiteStackGate stack)
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);
        var displayNames = new StringLocalizer<Maran.Modules.Sites.Resources.DisplayNames>(factory);

        return new GetPhpSettingsQueryHandler(
            new StubAccountDirectory(Snapshots.Account(accountId, "acme")),
            new PhpSettingsReader(context, displayNames),
            stack);
    }
}
