using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sftp.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The one thing that decides whether this product's translations are reachable at all: that an
/// error a customer is shown comes back in the language their panel asked for, over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <b>This was a live defect, and nothing in the suite could see it.</b> Every module ships
/// <c>ErrorMessages.ru.resx</c> and <c>ErrorMessages.hy.resx</c>; <c>maran structure</c> requires
/// every key to exist in all three; the satellite assemblies were built and shipped beside the
/// host. And a Russian panel still read every error in English, because
/// <c>app.UseExceptionHandling()</c> ran one line BEFORE <c>app.UsePanelLocalization()</c>.
/// </para>
/// <para>
/// The ordering is the whole mechanism, and it is not obvious. On the way IN, localisation runs
/// after the handler is installed, so by the time a controller throws, the culture IS set. But
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> is backed by an async-local: a
/// value assigned in a nested flow does not propagate back OUT to the frame that catches. So the
/// exception handler resolved its text in the parent context, where no culture had ever been set,
/// and <c>ResourceManager</c> fell back to the neutral English resx — silently, because a fallback
/// is what a resource manager is supposed to do.
/// </para>
/// <para>
/// Which is why this test lives here and not in a module: it is not about one message. It asks two
/// different modules for two different failures in two different languages, because the defect was
/// in the pipeline every module's answer passes through, and a single-module test would have read as
/// a translation problem rather than an ordering one.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ErrorMessageLocalizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The neutral resx text of the FTPS passive-address refusal, in full.</summary>
    private const string PassiveAddressEnglish =
        "The passive address must be an IPv4 address written plainly";

    /// <summary>The Russian resx text of the same refusal.</summary>
    private const string PassiveAddressRussian =
        "Пассивный адрес должен быть адресом IPv4 в обычной записи";

    /// <summary>The Armenian resx text of the same refusal.</summary>
    private const string PassiveAddressArmenian = "Պասիվ հասցեն պետք է լինի սովորական գրառմամբ IPv4 հասցե";

    /// <summary>The Russian resx text of an SFTP login-name refusal, from a different module.</summary>
    private const string SftpNameRussian = "Имя пользователя SFTP может содержать только строчные латинские буквы";

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ErrorMessageLocalizationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Each supported language, with the text that language's resx holds.</summary>
    public static TheoryData<string, string> PassiveAddressRefusalPerLanguage()
    {
        return new TheoryData<string, string>
        {
            { "en", PassiveAddressEnglish },
            { "ru", PassiveAddressRussian },
            { "hy", PassiveAddressArmenian },
        };
    }

    /// <summary>A refusal comes back in the language the panel asked for, in every language shipped.</summary>
    /// <param name="language">The <c>Accept-Language</c> the panel sends.</param>
    /// <param name="expected">The opening of that language's own resx value.</param>
    [Theory]
    [MemberData(nameof(PassiveAddressRefusalPerLanguage))]
    public async Task A_refusal_is_written_in_the_language_the_panel_asked_for(string language, string expected)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var detail = await RefusedPassiveAddressDetailAsync(client);

        Assert.Contains(expected, detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three answers are three different strings, so the theory above cannot be satisfied by a
    /// pipeline that returns one language under every header.
    /// </summary>
    /// <remarks>
    /// This is the guard that made the original defect visible: English was a CORRECT answer for
    /// <c>en</c>, so a test asserting only "the detail is not empty", or only the English case,
    /// passed against a panel that never translated anything.
    /// </remarks>
    [Fact]
    public async Task The_three_languages_do_not_all_answer_with_the_same_words()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        var answers = new List<string>();
        foreach (var language in new[] { "en", "ru", "hy" })
        {
            using var client = await SignInAsync(factory, "admin");
            client.DefaultRequestHeaders.AcceptLanguage.Clear();
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
            answers.Add(await RefusedPassiveAddressDetailAsync(client));
        }

        Assert.Equal(3, answers.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A second module's refusal is translated too, because the mechanism is the pipeline and not
    /// one module's resources.
    /// </summary>
    [Fact]
    public async Task A_second_modules_refusal_is_translated_by_the_same_pipeline()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var accountId = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("ru"));

        var response = await client.PostAsJsonAsync(
            "/api/v1/sftp-users", new { AccountId = accountId, Name = "BAD NAME" });

        Assert.Contains(SftpNameRussian, await DetailOfAsync(response), StringComparison.Ordinal);
    }

    /// <summary>
    /// A header naming a language this panel does not ship falls back to the neutral resx rather
    /// than to an empty message or a resource key.
    /// </summary>
    [Fact]
    public async Task A_language_this_panel_does_not_ship_falls_back_to_english()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("de"));

        var detail = await RefusedPassiveAddressDetailAsync(client);

        Assert.Contains(PassiveAddressEnglish, detail, StringComparison.Ordinal);
    }

    /// <summary>Asks the FTPS surface to accept an IPv6 passive address and returns the refusal's detail.</summary>
    /// <param name="client">A signed-in client carrying the language under test.</param>
    /// <returns>The <c>detail</c> member of the problem document.</returns>
    private static async Task<string> RefusedPassiveAddressDetailAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/ftps-server/enable",
            new { Hostname = "ftps.example.test", PassiveAddress = "2001:db8::1" });

        return await DetailOfAsync(response);
    }

    /// <summary>Reads the <c>detail</c> member out of a problem document.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The detail text, which the panel shows the operator verbatim.</returns>
    private static async Task<string> DetailOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("detail").GetString()!;
    }

    /// <summary>Boots the host against this test's own database.</summary>
    /// <returns>The configured factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>Applies the migrations the two surfaces under test need.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<FtpDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one plan, one account and one administrator.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The seeded account's identifier.</returns>
    private static async Task<Guid> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var account = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        await identity.SaveChangesAsync();

        return account.Id;
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    /// <returns>A client whose requests are authenticated.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
