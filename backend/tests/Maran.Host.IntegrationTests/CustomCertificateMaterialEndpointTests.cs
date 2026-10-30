using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Customer-supplied certificate material that is missing, is not PEM, or that the agent refuses is
/// answered 400 over real HTTP with a code naming the field and the problem, and a sentence in the
/// caller's language — never as "required" for text that was sent (issue #214).
/// </summary>
/// <remarks>
/// The agent is replaced by <see cref="RefusingSslAgent"/>, which answers every install with the
/// code it was built with, so the agent's three verdicts are driven through the module, the result
/// translation and the localization pipeline exactly as the real client's codes would be. That the
/// real client produces those codes from the wire is <c>AgentSslClientTests</c>' to prove.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class CustomCertificateMaterialEndpointTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The domain of the site the signed-in customer owns.</summary>
    private const string OwnDomain = "own.example.com";

    /// <summary>Certificate text the validator accepts.</summary>
    private const string ArmouredCertificate = "-----BEGIN CERTIFICATE-----\nleaf\n-----END CERTIFICATE-----";

    /// <summary>Key text the validator accepts.</summary>
    private const string ArmouredKey = "-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public CustomCertificateMaterialEndpointTests(PostgresFixture postgres)
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

    /// <summary>Material the validator refuses is answered with the code for that field and problem.</summary>
    /// <param name="certificatePem">The certificate text sent.</param>
    /// <param name="privateKeyPem">The key text sent.</param>
    /// <param name="code">The code the panel must answer with.</param>
    [Theory]
    [InlineData("", ArmouredKey, "CertificatePemRequired")]
    [InlineData("not a certificate", ArmouredKey, "CertificatePemInvalid")]
    [InlineData(ArmouredCertificate, "", "PrivateKeyPemRequired")]
    [InlineData(ArmouredCertificate, "not a key", "PrivateKeyPemInvalid")]
    public async Task Refused_material_is_answered_400_with_the_code_for_that_field_and_problem(
        string certificatePem,
        string privateKeyPem,
        string code)
    {
        var agent = new RefusingSslAgent(null);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await PostCustomAsync(client, certificatePem, privateKeyPem);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await ProblemOfAsync(response)).Code);
        Assert.Equal(0, agent.Installs);
    }

    /// <summary>Each of the agent's verdicts on the material reaches the caller as its own code, as a 400.</summary>
    /// <param name="code">The code the agent answers with.</param>
    [Theory]
    [InlineData("AgentCertificateUnreadable")]
    [InlineData("AgentPrivateKeyUnreadable")]
    [InlineData("AgentPrivateKeyMismatch")]
    public async Task Each_agent_verdict_on_the_material_is_answered_400_with_its_own_code(string code)
    {
        var agent = new RefusingSslAgent(Error.Of(code, ErrorType.Validation));
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await PostCustomAsync(client, ArmouredCertificate, ArmouredKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await ProblemOfAsync(response)).Code);
        Assert.Equal(1, agent.Installs);
    }

    /// <summary>The refusal is a translated sentence in the caller's language, never the machine code.</summary>
    /// <param name="language">The Accept-Language sent.</param>
    /// <param name="expected">The resx text for that language.</param>
    [Theory]
    [InlineData("en", "The private key does not belong to this certificate. Paste the key that was created together with the certificate request.")]
    [InlineData("ru", "Закрытый ключ не соответствует этому сертификату. Вставьте ключ, созданный вместе с запросом на сертификат.")]
    [InlineData("hy", "Գաղտնի բանալին չի համապատասխանում այս սերտիֆիկատին։ Տեղադրեք այն բանալին, որը ստեղծվել է սերտիֆիկատի հարցման հետ միասին։")]
    public async Task The_refusal_is_a_sentence_in_the_callers_language(string language, string expected)
    {
        var agent = new RefusingSslAgent(Error.Of("AgentPrivateKeyMismatch", ErrorType.Validation));
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory);
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var response = await PostCustomAsync(client, ArmouredCertificate, ArmouredKey);

        Assert.Equal(expected, (await ProblemOfAsync(response)).Detail);
    }

    /// <summary>A pair the agent accepts is installed: the inverse control for every refusal above.</summary>
    [Fact]
    public async Task A_pair_the_agent_accepts_is_installed()
    {
        var agent = new RefusingSslAgent(null);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory);

        var response = await PostCustomAsync(client, ArmouredCertificate, ArmouredKey);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, agent.Installs);
    }

    /// <summary>Posts custom material for the customer's own domain.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <param name="certificatePem">The certificate text.</param>
    /// <param name="privateKeyPem">The key text.</param>
    /// <returns>The response.</returns>
    private static Task<HttpResponseMessage> PostCustomAsync(HttpClient client, string certificatePem, string privateKeyPem)
    {
        return client.PostAsJsonAsync("/api/v1/certificates/custom", new
        {
            domain = OwnDomain,
            certificatePem,
            privateKeyPem,
        });
    }

    /// <summary>Reads the code and the detail out of a problem document.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The machine code and the localized sentence.</returns>
    private static async Task<(string Code, string Detail)> ProblemOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (
            body.RootElement.GetProperty("code").GetString()!,
            body.RootElement.GetProperty("detail").GetString()!);
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the TLS agent replaced.</summary>
    /// <param name="agent">The agent double every certificate call reaches.</param>
    /// <returns>The booted factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(RefusingSslAgent agent)
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

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentSslClient>(agent);
            });
        });
    }

    /// <summary>Applies every module's migrations these tests need, the way the installer does.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SslDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one customer owning an account with one site.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var account = new Account(Guid.NewGuid(), "own", OwnDomain, planId, now);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(account.Id);
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        sites.Sites.Add(new Site(
            Guid.NewGuid(),
            account.Id,
            OwnDomain,
            [],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            $"/home/own/sites/{OwnDomain}",
            now));
        await sites.SaveChangesAsync();
    }

    /// <summary>Signs the customer in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>An authenticated client.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = "customer", Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>A TLS agent that answers every install with one fixed outcome and counts the calls.</summary>
    /// <param name="failure">The error to answer with, or null to install successfully.</param>
    private sealed class RefusingSslAgent(Error? failure) : IAgentSslClient
    {
        /// <summary>The number of install calls that reached this agent.</summary>
        private int _installs;

        /// <summary>Gets the number of install calls that reached this agent.</summary>
        public int Installs
        {
            get { return Volatile.Read(ref _installs); }
        }

        /// <inheritdoc />
        public Task<Result<InstalledCertificateDto>> InstallCertificateAsync(
            string accountUsername,
            string domain,
            string certificatePem,
            string privateKeyPem,
            SiteDescriptor site,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _installs);

            return Task.FromResult(failure is null
                ? Result<InstalledCertificateDto>.Ok(new InstalledCertificateDto(DateTimeOffset.UnixEpoch.AddYears(60)))
                : Result<InstalledCertificateDto>.Fail(failure));
        }

        /// <inheritdoc />
        public Task<Result<bool>> RemoveCertificateAsync(
            string accountUsername,
            string domain,
            SiteDescriptor site,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<bool>.Ok(true));
        }
    }
}
