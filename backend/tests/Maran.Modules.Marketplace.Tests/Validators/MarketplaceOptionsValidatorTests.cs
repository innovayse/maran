using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Marketplace.Tests.Validators;

/// <summary>The marketplace options accept the documented defaults and refuse an address a URL should not carry.</summary>
public sealed class MarketplaceOptionsValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly MarketplaceOptionsValidator _validator = new();

    /// <summary>Runs the validator over options.</summary>
    /// <param name="options">The options to judge.</param>
    /// <returns>The verdict.</returns>
    private ValidateOptionsResult Judge(MarketplaceOptions options)
    {
        return _validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);
    }

    /// <summary>The options a server that sets nothing runs with are accepted, and name the documented host.</summary>
    [Fact]
    public void The_defaults_are_accepted_and_name_the_documented_host()
    {
        var options = new MarketplaceOptions();

        Assert.True(Judge(options).Succeeded);
        Assert.Equal("https://api-marketplace-maran.innovayse.com", options.BaseUrl);
        Assert.Equal("stable", options.Channel);
        Assert.Equal("/usr/local/maran/keys/marketplace-metadata.pub", options.MetadataKeyPath);
        Assert.Equal("/var/lib/maran/marketplace", options.PackageDirectory);
        Assert.Equal(30, options.RequestTimeoutSeconds);
        Assert.Equal(4L * 1024 * 1024, options.MaxCatalogueBytes);
        Assert.Equal(16L * 1024, options.MaxGrantBytes);
        Assert.Equal(256L * 1024 * 1024, options.MaxPackageBytes);
        Assert.Equal(24, options.CatalogueMaxAgeHours);
        Assert.Equal(10, options.CacheMinutes);
    }

    /// <summary>The positive control: a valid custom https host is accepted, so the refusals below are not a validator that refuses everything.</summary>
    [Theory]
    [InlineData("https://market.example.org")]
    [InlineData("https://market.example.org/")]
    [InlineData("https://market.example.org:8443")]
    [InlineData("https://market.example.org/api")]
    public void A_valid_custom_https_host_is_accepted(string baseUrl)
    {
        var options = new MarketplaceOptions { BaseUrl = baseUrl };

        Assert.True(Judge(options).Succeeded);
    }

    /// <summary>An address that is not https, not absolute, or carries user information, a query or a fragment is refused.</summary>
    /// <param name="baseUrl">The configured address.</param>
    [Theory]
    [InlineData("http://api-marketplace-maran.innovayse.com")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("ftp://market.example.org")]
    [InlineData("//market.example.org")]
    [InlineData("market.example.org")]
    [InlineData("")]
    [InlineData("https://user@market.example.org")]
    [InlineData("https://user:secret@market.example.org")]
    [InlineData("https://market.example.org?token=abc")]
    [InlineData("https://market.example.org/?")]
    [InlineData("https://market.example.org/#frag")]
    [InlineData("https://market.example.org/a b")]
    [InlineData("https://market.example.org/\n")]
    public void An_address_that_is_not_a_plain_https_origin_is_refused(string baseUrl)
    {
        var verdict = Judge(new MarketplaceOptions { BaseUrl = baseUrl });

        Assert.True(verdict.Failed);
        Assert.Contains("Marketplace:BaseUrl", verdict.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>A relative key path or package directory is refused, and an absolute one is accepted.</summary>
    [Fact]
    public void A_relative_path_is_refused_and_an_absolute_one_is_accepted()
    {
        Assert.True(Judge(new MarketplaceOptions { MetadataKeyPath = "keys/marketplace.pub" }).Failed);
        Assert.True(Judge(new MarketplaceOptions { PackageDirectory = "" }).Failed);
        Assert.True(Judge(new MarketplaceOptions { PackageDirectory = "/tmp/maran-test/marketplace" }).Succeeded);
    }

    /// <summary>A channel that is not a short lower-case word is refused.</summary>
    /// <param name="channel">The configured channel.</param>
    [Theory]
    [InlineData("")]
    [InlineData("Stable")]
    [InlineData("1stable")]
    [InlineData("stable/../beta")]
    [InlineData("a-channel-name-that-is-far-too-long-to-be-plain")]
    public void A_channel_that_is_not_a_plain_word_is_refused(string channel)
    {
        Assert.True(Judge(new MarketplaceOptions { Channel = channel }).Failed);
        Assert.True(Judge(new MarketplaceOptions { Channel = "beta-2" }).Succeeded);
    }

    /// <summary>No bound may be zero, negative, or above its ceiling; the package ceiling is the package reader's total.</summary>
    [Fact]
    public void A_bound_outside_its_range_is_refused_and_one_at_its_ceiling_is_accepted()
    {
        Assert.True(Judge(new MarketplaceOptions { RequestTimeoutSeconds = 0 }).Failed);
        Assert.True(Judge(new MarketplaceOptions { MaxCatalogueBytes = -1 }).Failed);
        Assert.True(Judge(new MarketplaceOptions { MaxGrantBytes = MarketplaceOptions.GrantBytesCeiling + 1 }).Failed);
        Assert.True(Judge(new MarketplaceOptions { MaxPackageBytes = MarketplaceOptions.PackageBytesCeiling + 1 }).Failed);
        Assert.True(Judge(new MarketplaceOptions { CatalogueMaxAgeHours = 0 }).Failed);
        Assert.True(Judge(new MarketplaceOptions { CacheMinutes = MarketplaceOptions.CacheMinutesCeiling + 1 }).Failed);

        Assert.True(Judge(new MarketplaceOptions { MaxPackageBytes = MarketplaceOptions.PackageBytesCeiling }).Succeeded);
        Assert.True(Judge(new MarketplaceOptions { RequestTimeoutSeconds = MarketplaceOptions.RequestTimeoutSecondsCeiling }).Succeeded);
    }

    /// <summary>Every wrong setting is named in one verdict, so an operator fixes them in one pass.</summary>
    [Fact]
    public void Every_wrong_setting_is_named_in_one_verdict()
    {
        var verdict = Judge(new MarketplaceOptions { BaseUrl = "http://x", Channel = "", CacheMinutes = 0 });

        Assert.Contains("Marketplace:BaseUrl", verdict.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Marketplace:Channel", verdict.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Marketplace:CacheMinutes", verdict.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>The validator is lazy: the first read of the options runs it, and a bad address throws then and not before.</summary>
    [Fact]
    public void The_options_are_validated_on_first_read_and_not_when_they_are_built()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<MarketplaceOptions>, MarketplaceOptionsValidator>();
        services.Configure<MarketplaceOptions>(options =>
        {
            options.BaseUrl = "http://not-https.example.org";
        });
        using var provider = services.BuildServiceProvider();

        var holder = provider.GetRequiredService<IOptions<MarketplaceOptions>>();

        Assert.Throws<OptionsValidationException>(() => { _ = holder.Value; });
    }
}
