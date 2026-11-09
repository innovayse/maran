using System.Globalization;
using System.Resources;
using Maran.Host.Demo.Options;
using Maran.Host.Demo.Reset;
using Maran.Host.Demo.Resources;
using Maran.Host.Demo.World;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Tests.Reset;

/// <summary>What the status endpoint tells every visitor: cadence, reset times, sign-ins and reasons.</summary>
public sealed class DemoStatusProviderTests : IDisposable
{
    /// <summary>
    /// Every area id a refusal can carry, spelled as the SPA expects them: the seven it knows by name, then the
    /// five it shows the generic wording for.
    /// </summary>
    private static readonly string[] SpaAreas =
    [
        "securityPolicy",
        "smtp",
        "panelCertificate",
        "panelUpdate",
        "twoFactor",
        "apiKeys",
        "licence",
        "credentials",
        "accessPaths",
        "externalServices",
        "protectedResource",
        "unclassified",
    ];

    /// <summary>A directory of this test's own.</summary>
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "maran-demo-status-" + Guid.NewGuid().ToString("N"));

    /// <summary>Removes the test's directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Before any reset is recorded both instants are null rather than invented.</summary>
    [Fact]
    public async Task Without_a_recorded_reset_both_instants_are_null()
    {
        var status = await Provider().GetAsync(CancellationToken.None);

        Assert.Null(status.LastResetAt);
        Assert.Null(status.NextResetAt);
        Assert.Equal(3, status.IntervalHours);
    }

    /// <summary>The next reset is the last one plus the configured number of hours.</summary>
    [Fact]
    public async Task The_next_reset_is_the_last_plus_the_interval()
    {
        var last = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);
        await Marker().WriteAsync(last, CancellationToken.None);

        var status = await Provider().GetAsync(CancellationToken.None);

        Assert.Equal(last, status.LastResetAt);
        Assert.Equal(last.AddHours(3), status.NextResetAt);
    }

    /// <summary>The two published sign-ins carry the roles the SPA expects and the configured secrets.</summary>
    [Fact]
    public async Task The_published_sign_ins_are_the_administrator_and_the_featured_customer()
    {
        var status = await Provider().GetAsync(CancellationToken.None);

        Assert.Collection(
            status.Identities,
            admin =>
            {
                Assert.Equal("administrator", admin.Role);
                Assert.Equal("demoadmin", admin.Username);
                Assert.Equal("admin-password-1", admin.Password);
            },
            customer =>
            {
                Assert.Equal("customer", customer.Role);
                Assert.Equal(StandardDemoWorld.FeaturedCustomerAccount, customer.Username);
                Assert.Equal("customer-password-1", customer.Password);
            });
    }

    /// <summary>The reasons cover every refusal area, each with a sentence.</summary>
    [Fact]
    public async Task The_refusal_reasons_are_every_refusal_area()
    {
        var status = await Provider().GetAsync(CancellationToken.None);

        Assert.Equal(SpaAreas, status.RefusalReasons.Select(reason => { return reason.Area; }));
        Assert.All(status.RefusalReasons, reason => { Assert.Equal("text:" + reason.Area, reason.Reason); });
    }

    /// <summary>
    /// The shipped resource tables carry a different sentence per area in English, Russian and Armenian.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void Every_area_has_a_sentence_in_every_language(string language)
    {
        var table = new ResourceManager(typeof(DemoMessages).FullName!, typeof(DemoMessages).Assembly);
        var english = new ResourceManager(typeof(DemoMessages).FullName!, typeof(DemoMessages).Assembly);

        foreach (var area in SpaAreas)
        {
            var text = table.GetString(area, CultureInfo.GetCultureInfo(language));

            Assert.False(string.IsNullOrWhiteSpace(text), $"{language}: {area}");
            if (language != "en")
            {
                Assert.NotEqual(english.GetString(area, CultureInfo.GetCultureInfo("en")), text);
            }
        }
    }

    /// <summary>Builds the marker over this test's directory.</summary>
    /// <returns>The marker.</returns>
    private DemoResetMarker Marker()
    {
        return new DemoResetMarker(
            new OptionsWrapper<DemoResetOptions>(
                new DemoResetOptions { MarkerPath = Path.Combine(_directory, "last-reset") }));
    }

    /// <summary>Builds the provider over fixed settings and a localizer that echoes the key.</summary>
    /// <returns>The provider.</returns>
    private DemoStatusProvider Provider()
    {
        return new DemoStatusProvider(
            Marker(),
            new OptionsWrapper<DemoOptions>(new DemoOptions { ResetIntervalHours = 3 }),
            new OptionsWrapper<DemoIdentityOptions>(new DemoIdentityOptions
            {
                AdministratorUsername = "demoadmin",
                AdministratorPassword = "admin-password-1",
                CustomerPassword = "customer-password-1",
            }),
            new KeyEchoLocalizer());
    }

    /// <summary>A localizer whose text is the key behind a marker, so a test sees which key was asked for.</summary>
    private sealed class KeyEchoLocalizer : IStringLocalizer<DemoMessages>
    {
        /// <inheritdoc />
        public LocalizedString this[string name]
        {
            get { return new LocalizedString(name, "text:" + name); }
        }

        /// <inheritdoc />
        public LocalizedString this[string name, params object[] arguments]
        {
            get { return this[name]; }
        }

        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return [];
        }
    }
}
