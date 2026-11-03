using System.Text.Json;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.GetSiteChoices;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Tests.TestSupport;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Sites.Tests.Queries.GetSiteChoices;

/// <summary>Behavioral contract of <see cref="GetSiteChoicesQueryHandler"/>.</summary>
public sealed class GetSiteChoicesQueryHandlerTests
{
    /// <summary>The choices list every backend type the entity knows, spelled as the API serializes it.</summary>
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
        Assert.Equal(5, labels.Count);
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

    /// <summary>Runs the handler with the module's real resource table.</summary>
    /// <returns>The choices it answered.</returns>
    private static async Task<Common.SiteChoicesDto> HandleAsync()
    {
        var localizer = new StringLocalizer<DisplayNames>(
            new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance));
        var handler = new GetSiteChoicesQueryHandler(localizer);

        var result = await handler.HandleAsync(new GetSiteChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
