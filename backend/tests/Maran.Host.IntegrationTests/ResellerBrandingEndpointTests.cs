using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The branding a reseller gives its customers' shell, over real HTTP against a real PostgreSQL
/// (issue #367): a reseller sets, reads and clears its own, a bad value is refused with the code that
/// says why, another reseller never sees it, an administrator is refused the reseller surface, and the
/// change is journalled without carrying the values.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the customer's own read (<c>GET /api/v1/accounts/me/branding</c>) with a customer
/// token. The world seeds accounts without an owner login to sign in, so that read is proved against the
/// tenant filter in the Accounts unit tests; this class checks only that a caller with no account is
/// answered with the panel's own and not a failure.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerBrandingEndpointTests : IAsyncLifetime
{
    /// <summary>The reseller endpoint.</summary>
    private const string ResellerEndpoint = "/api/v1/reseller/branding";

    /// <summary>The shell's endpoint.</summary>
    private const string ShellEndpoint = "/api/v1/accounts/me/branding";

    /// <summary>A real 1x1 PNG, as a data URI.</summary>
    private const string PngLogo =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerBrandingEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A reseller that set nothing reads every member empty, not a failure.</summary>
    [Fact]
    public async Task A_reseller_that_set_nothing_reads_an_empty_branding()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        using var body = JsonDocument.Parse(await alpha.GetStringAsync(ResellerEndpoint));

        Assert.All(["displayName", "accentColor", "logo"], member =>
        {
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty(member).ValueKind);
        });
    }

    /// <summary>A reseller sets its branding, reads it back, and the change is journalled without the values.</summary>
    [Fact]
    public async Task A_reseller_sets_its_branding_and_reads_it_back()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var put = await alpha.PutAsJsonAsync(
            ResellerEndpoint,
            new { displayName = "Alpha Hosting", accentColor = "#2468DC", logo = PngLogo });
        using var body = JsonDocument.Parse(await alpha.GetStringAsync(ResellerEndpoint));

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("Alpha Hosting", body.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("#2468dc", body.RootElement.GetProperty("accentColor").GetString());
        Assert.Equal(PngLogo, body.RootElement.GetProperty("logo").GetString());
        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerBrandingChanged));
        Assert.Equal(("alpha", "alpha", true), (entry.ActorUsername, entry.Subject, entry.Succeeded));
    }

    /// <summary>Setting nothing removes the branding.</summary>
    [Fact]
    public async Task Setting_nothing_clears_the_branding()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        await alpha.PutAsJsonAsync(ResellerEndpoint, new { displayName = "Alpha Hosting" });

        var cleared = await alpha.PutAsJsonAsync(ResellerEndpoint, new { });
        using var body = JsonDocument.Parse(await alpha.GetStringAsync(ResellerEndpoint));

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("displayName").ValueKind);
    }

    /// <summary>A value that breaks a rule is refused with 400 and the code that says why; nothing is stored.</summary>
    /// <param name="kind">Which value is bad.</param>
    /// <param name="code">The code the refusal must carry.</param>
    [Theory]
    [InlineData("name", "BrandingDisplayNameInvalid")]
    [InlineData("color", "BrandingAccentColorInvalid")]
    [InlineData("light", "BrandingAccentColorLowContrast")]
    [InlineData("logo", "BrandingLogoInvalid")]
    public async Task A_bad_value_is_refused_with_the_code_that_says_why(string kind, string code)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        object body = kind switch
        {
            "name" => new { displayName = new string('a', 41) },
            "color" => new { accentColor = "blue" },
            "light" => new { accentColor = "#ffcc00" },
            _ => new { logo = "data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=" },
        };

        var response = await alpha.PutAsJsonAsync(ResellerEndpoint, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemDocument.CodeAsync(response));
        Assert.Empty(await world.AuditEntriesAsync(AccountAuditActions.ResellerBrandingChanged));
    }

    /// <summary>One reseller never reads or changes another's: bravo reads its own empty branding after alpha set one.</summary>
    [Fact]
    public async Task Another_reseller_does_not_see_the_branding()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");
        await alpha.PutAsJsonAsync(ResellerEndpoint, new { displayName = "Alpha Hosting" });

        using var body = JsonDocument.Parse(await bravo.GetStringAsync(ResellerEndpoint));

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("displayName").ValueKind);
    }

    /// <summary>An administrator is refused the reseller surface: it owns no reseller to brand.</summary>
    [Fact]
    public async Task An_administrator_is_refused_the_reseller_surface()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var read = await admin.GetAsync(ResellerEndpoint);
        var write = await admin.PutAsJsonAsync(ResellerEndpoint, new { displayName = "Nope" });

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    /// <summary>A caller with no account reads the panel's own from the shell's endpoint, not a failure.</summary>
    [Fact]
    public async Task A_caller_with_no_account_reads_the_panels_own_from_the_shell_endpoint()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        await alpha.PutAsJsonAsync(ResellerEndpoint, new { displayName = "Alpha Hosting" });

        using var body = JsonDocument.Parse(await alpha.GetStringAsync(ShellEndpoint));

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("displayName").ValueKind);
    }
}
