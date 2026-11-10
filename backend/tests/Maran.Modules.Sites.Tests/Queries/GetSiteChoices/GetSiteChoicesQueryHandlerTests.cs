using System.Text.Json;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.GetSiteChoices;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Apps;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Sites.Tests.Queries.GetSiteChoices;

/// <summary>Behavioral contract of <see cref="GetSiteChoicesQueryHandler"/>.</summary>
public sealed class GetSiteChoicesQueryHandlerTests
{
    /// <summary>With every runtime installed the choices list every backend type the entity knows, spelled as the API serializes it.</summary>
    [Fact]
    public async Task The_choices_list_every_backend_type_spelled_as_the_api_serializes_it()
    {
        var result = await HandleAsync();

        var expected = Enum.GetNames<SiteBackendType>().Select(name =>
        {
            return JsonNamingPolicy.CamelCase.ConvertName(name);
        });
        Assert.Equal(expected, result.BackendTypes.Select(choice =>
        {
            return choice.Value;
        }));
    }

    /// <summary>The choices list every serving state the entity knows, spelled as the API serializes it.</summary>
    [Fact]
    public async Task The_choices_list_every_serving_state_spelled_as_the_api_serializes_it()
    {
        var result = await HandleAsync();

        var expected = Enum.GetNames<SiteStatus>().Select(name =>
        {
            return JsonNamingPolicy.CamelCase.ConvertName(name);
        });
        Assert.Equal(expected, result.Statuses.Select(choice =>
        {
            return choice.Value;
        }));
    }

    /// <summary>Every label is written in the caller's language and none falls back to its resource key.</summary>
    /// <param name="culture">The caller's culture.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task Every_label_is_written_in_the_callers_language_and_none_falls_back_to_its_key(string culture)
    {
        using var scope = new CultureScope(culture);

        var result = await HandleAsync();

        var labels = result.BackendTypes.Concat(result.Statuses).Select(choice =>
        {
            return choice.Label;
        }).ToList();
        Assert.Equal(6, labels.Count);
        Assert.All(labels, label =>
        {
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.DoesNotContain("SiteBackendType", label);
            Assert.DoesNotContain("SiteStatus", label);
        });
    }

    /// <summary>The Russian reverse-proxy label is the translated one, so the table is really per language.</summary>
    [Fact]
    public async Task The_russian_reverse_proxy_label_is_the_translated_one()
    {
        using var scope = new CultureScope("ru");

        var result = await HandleAsync();

        var proxy = Assert.Single(result.BackendTypes, choice =>
        {
            return choice.Value == "reverseProxy";
        });
        Assert.Equal("Обратный прокси", proxy.Label);
    }

    /// <summary>The traffic windows are the ones the validator accepts, the default first.</summary>
    [Fact]
    public async Task The_traffic_windows_are_the_ones_the_validator_accepts_the_default_first()
    {
        var result = await HandleAsync();

        Assert.Equal(["7", "30", "90"], result.TrafficWindows.Select(choice => { return choice.Value; }));
    }

    /// <summary>The log retentions run from one day to the installer's ceiling, fewest first.</summary>
    [Fact]
    public async Task The_log_retentions_run_from_one_day_to_the_installers_ceiling()
    {
        var result = await HandleAsync();

        Assert.Equal(
            Enumerable.Range(1, 14).Select(day => { return day.ToString(System.Globalization.CultureInfo.InvariantCulture); }),
            result.LogRetentions.Select(choice => { return choice.Value; }));
    }

    /// <summary>Every window and retention label is written in the caller's language and declines the number where the language does.</summary>
    /// <param name="culture">The caller's culture.</param>
    /// <param name="one">The label of one day.</param>
    /// <param name="few">The label of three days.</param>
    /// <param name="many">The label of fourteen days.</param>
    [Theory]
    [InlineData("en", "1 day", "3 days", "14 days")]
    [InlineData("ru", "1 день", "3 дня", "14 дней")]
    [InlineData("hy", "1 օր", "3 օր", "14 օր")]
    public async Task Retention_labels_are_declined_for_the_number_in_the_callers_language(
        string culture, string one, string few, string many)
    {
        using var scope = new CultureScope(culture);

        var result = await HandleAsync();

        Assert.Equal(one, result.LogRetentions.Single(choice => { return choice.Value == "1"; }).Label);
        Assert.Equal(few, result.LogRetentions.Single(choice => { return choice.Value == "3"; }).Label);
        Assert.Equal(many, result.LogRetentions.Single(choice => { return choice.Value == "14"; }).Label);
        Assert.All(result.TrafficWindows, window => { Assert.DoesNotContain("SiteTrafficWindow", window.Label); });
    }

    /// <summary>A backend whose runtime is not installed is not offered, and the others still are.</summary>
    /// <param name="phpInstalled">Whether a PHP version is installed.</param>
    /// <param name="nodeInstalled">Whether a Node major is installed.</param>
    /// <param name="expected">The backends the form may offer.</param>
    [Theory]
    [InlineData(true, true, "static,php,reverseProxy,node")]
    [InlineData(false, true, "static,reverseProxy,node")]
    [InlineData(true, false, "static,php,reverseProxy")]
    [InlineData(false, false, "static,reverseProxy")]
    public async Task A_backend_whose_runtime_is_not_installed_is_not_offered(
        bool phpInstalled,
        bool nodeInstalled,
        string expected)
    {
        var stack = SiteStacks.With(
            StackWebServer.Nginx,
            phpInstalled ? ["8.3"] : [],
            nodeInstalled ? ["22"] : []);

        var result = await HandleAsync(stack);

        Assert.Equal(expected.Split(','), result.BackendTypes.Select(choice => { return choice.Value; }));
    }

    /// <summary>Only the installed versions are offered, newest first, valued and labelled by the version.</summary>
    [Fact]
    public async Task Only_the_installed_versions_are_offered_newest_first()
    {
        var stack = SiteStacks.With(StackWebServer.Nginx, ["8.1", "8.10", "8.3"], ["20", "22"]);

        var result = await HandleAsync(stack);

        Assert.Equal(["8.10", "8.3", "8.1"], result.PhpVersions.Select(choice => { return choice.Value; }));
        Assert.Equal(["22", "20"], result.NodeVersions.Select(choice => { return choice.Label; }));
    }

    /// <summary>The installed web server is reported by name, and none is reported as absent.</summary>
    /// <param name="installed">The installed web server.</param>
    /// <param name="value">The value the form receives, or null for none.</param>
    [Theory]
    [InlineData(StackWebServer.Nginx, "nginx")]
    [InlineData(StackWebServer.Apache, "apache")]
    [InlineData(StackWebServer.None, null)]
    public async Task The_installed_web_server_is_reported_and_none_is_reported_as_absent(
        StackWebServer installed,
        string? value)
    {
        var result = await HandleAsync(SiteStacks.With(installed, [], []));

        Assert.Equal(value, result.WebServer?.Value);
    }

    /// <summary>Runs the handler with the module's real resource table.</summary>
    /// <param name="stack">The installed stack; a host with nginx and every runtime when omitted.</param>
    /// <returns>The choices it answered.</returns>
    private static async Task<Common.SiteChoicesDto> HandleAsync(SiteStackGate? stack = null)
    {
        var localizer = new StringLocalizer<DisplayNames>(
            new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance));
        var handler = new GetSiteChoicesQueryHandler(new SiteDisplayNames(localizer), stack ?? SiteStacks.Ready());

        var result = await handler.HandleAsync(new GetSiteChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
