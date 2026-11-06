using System.Globalization;
using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Queries.GetAcmeDirectory;
using Maran.Modules.Ssl.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Tests.Queries.GetAcmeDirectory;

/// <summary>
/// What the panel reports about the ACME authority it is configured with: the URL as configured, its
/// host, its kind, and the kind's name in the caller's language, read from the module's real resources.
/// </summary>
public sealed class GetAcmeDirectoryQueryHandlerTests
{
    /// <summary>A custom directory is answered with its URL, its host and the custom kind.</summary>
    [Fact]
    public async Task A_custom_directory_is_answered_with_its_url_host_and_kind()
    {
        var result = await HandleAsync("https://ca.internal.example:14000/dir", "en");

        Assert.Equal("https://ca.internal.example:14000/dir", result.Url);
        Assert.Equal("ca.internal.example", result.Host);
        Assert.Equal("custom", result.Kind);
        Assert.Equal("Custom authority", result.KindName);
    }

    /// <summary>The staging authority is named in English.</summary>
    [Fact]
    public async Task The_staging_authority_is_named_in_english()
    {
        var result = await HandleAsync("https://acme-staging-v02.api.letsencrypt.org/directory", "en");

        Assert.Equal("staging", result.Kind);
        Assert.Equal("Staging authority (certificates are not trusted)", result.KindName);
    }

    /// <summary>The production authority is named in Russian when the caller's culture is Russian.</summary>
    [Fact]
    public async Task The_production_authority_is_named_in_russian_for_a_russian_caller()
    {
        var result = await HandleAsync("https://acme-v02.api.letsencrypt.org/directory", "ru");

        Assert.Equal("production", result.Kind);
        Assert.Equal("acme-v02.api.letsencrypt.org", result.Host);
        Assert.Equal("Рабочий центр сертификации", result.KindName);
    }

    /// <summary>Every kind has a name in every language, so none reads as its own resource key.</summary>
    /// <param name="culture">The caller's culture.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task Every_kind_has_a_name_in_every_language(string culture)
    {
        var urls = new[]
        {
            "https://acme-v02.api.letsencrypt.org/directory",
            "https://acme-staging-v02.api.letsencrypt.org/directory",
            "https://ca.internal.example/dir",
        };

        var names = new List<string>();
        foreach (var url in urls)
        {
            names.Add((await HandleAsync(url, culture)).KindName);
        }

        Assert.All(names, name => { Assert.DoesNotContain("AcmeDirectoryKind", name, StringComparison.Ordinal); });
        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Runs the handler over the module's real display names in one culture.</summary>
    /// <param name="url">The configured directory URL.</param>
    /// <param name="culture">The caller's culture.</param>
    /// <returns>The answer.</returns>
    private static async Task<AcmeDirectoryDto> HandleAsync(string url, string culture)
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);
        var handler = new GetAcmeDirectoryQueryHandler(
            new OptionsWrapper<AcmeOptions>(new AcmeOptions { DirectoryUrl = url }),
            new StringLocalizer<DisplayNames>(factory));

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            return await handler.HandleAsync(new GetAcmeDirectoryQuery(), CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
