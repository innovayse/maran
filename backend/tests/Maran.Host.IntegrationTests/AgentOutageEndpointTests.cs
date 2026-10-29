using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.V1;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What a panel answers when the agent behind it is gone, over real HTTP, real PostgreSQL and the REAL
/// gRPC channel to a socket path nothing serves (issue #87).
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> A refused or timed-out agent call reached the API as an exception the mapping
/// never learned, so every agent-backed action answered a bare 500 <c>HostUnexpectedError</c> during an
/// outage, and a site creation that failed that way left no audit row. The fix maps the transport's
/// failure to the typed <c>AgentUnavailable</c> error of kind Unavailable, which the one status mapping
/// answers as 503.
/// </para>
/// <para>
/// <b>Why nothing here is a fake.</b> The earlier tests of this path threw a hand-made exception that
/// the transport never raises, and passed against code that failed against the real socket. These tests
/// pin <c>Agent:SocketPath</c> themselves (the shared harness would otherwise dial the machine's default
/// path, which on a developer machine may hold a real agent) and leave the client, the resilience
/// pipeline and the channel as shipped. The two shapes are a path that does not exist and a stale socket
/// file that refuses the connection.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AgentOutageEndpointTests : IAsyncLifetime
{
    /// <summary>The English text of <c>AgentUnavailable</c>, copied from <c>ErrorMessages.resx</c>.</summary>
    private const string UnavailableEnglish =
        "Your server is not answering right now, so this request could not be confirmed. Please try again in a minute, and contact support if it keeps happening.";

    /// <summary>The Russian text of <c>AgentUnavailable</c>.</summary>
    private const string UnavailableRussian =
        "Ваш сервер сейчас не отвечает, поэтому выполнение запроса не подтверждено. Повторите попытку через минуту, а если ошибка повторяется — обратитесь в поддержку.";

    /// <summary>The Armenian text of <c>AgentUnavailable</c>.</summary>
    private const string UnavailableArmenian =
        "Ձեր սերվերն այս պահին չի պատասխանում, ուստի հարցման կատարումը չի հաստատվել։ Կրկնեք փորձը մեկ րոպեից, իսկ եթե սխալը շարունակվում է՝ դիմեք աջակցման ծառայությանը։";

    /// <summary>The domain the failed creation is asked for.</summary>
    private const string Domain = "outage.example.com";

    /// <summary>This test's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The socket paths this test made, removed on disposal.</summary>
    private readonly List<UnreachableAgentSocket> _sockets = [];

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AgentOutageEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Both outage shapes, one row each.</summary>
    /// <returns>The shape names.</returns>
    public static TheoryData<string> Shapes()
    {
        return new TheoryData<string>
        {
            UnreachableAgentSocket.MissingShape,
            UnreachableAgentSocket.StaleShape,
        };
    }

    /// <summary>Each shipped language with the text its resx holds.</summary>
    /// <returns>The language and the expected text.</returns>
    public static TheoryData<string, string> TextPerLanguage()
    {
        return new TheoryData<string, string>
        {
            { "en", UnavailableEnglish },
            { "ru", UnavailableRussian },
            { "hy", UnavailableArmenian },
        };
    }

    /// <summary>Creates this test's database.</summary>
    /// <returns>Resolves once it exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Removes the socket paths.</summary>
    /// <returns>The completed task.</returns>
    public Task DisposeAsync()
    {
        foreach (var socket in _sockets)
        {
            socket.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// An agent-backed read answers 503 with the typed code and the localized text, whichever way the
    /// agent is gone.
    /// </summary>
    /// <param name="shape">The outage shape.</param>
    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task An_agent_operation_answers_503_agent_unavailable_when_the_agent_socket_is_gone(string shape)
    {
        var socket = NewSocket(shape);
        await using var factory = await BootAsync(socket);
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/metrics");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        Assert.Equal("AgentUnavailable", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(UnavailableEnglish, body.RootElement.GetProperty("title").GetString());
    }

    /// <summary>
    /// A cron read against an unreachable agent answers 503 as well: the cron module re-codes agent
    /// failures, and the outage must survive that translation rather than become a 500.
    /// </summary>
    /// <param name="shape">The outage shape.</param>
    /// <returns>Resolves when the assertions have been made.</returns>
    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task A_cron_read_answers_503_agent_unavailable_when_the_agent_socket_is_gone(string shape)
    {
        var socket = NewSocket(shape);
        await using var factory = await BootAsync(socket);
        var accountId = await SeedAccountAsync(factory);
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await client.GetAsync($"/api/v1/cron-entries?accountId={accountId}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AgentUnavailable", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// The 503 body says the server is not answering and nothing about HOW: no socket path and no
    /// exception text reach a caller.
    /// </summary>
    /// <param name="shape">The outage shape.</param>
    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task The_unavailable_answer_discloses_neither_the_socket_path_nor_the_exception(string shape)
    {
        var socket = NewSocket(shape);
        await using var factory = await BootAsync(socket);
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var raw = await (await client.GetAsync("/api/v1/monitoring/metrics")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(socket.SocketPath, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("agent.sock", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("SocketException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpRequestException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Connection refused", raw, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Positive control for the disclosure test above: the REAL exception the transport throws does
    /// carry the exception type names that test hunts for, so a body free of them was kept free by the
    /// panel and not by a message that never had them.
    /// </summary>
    /// <remarks>
    /// Measured: the transport's text names <c>HttpRequestException</c>, <c>SocketException</c> and the
    /// host's socket error, but NOT the socket path — the connect failure is reported against the
    /// placeholder authority <c>(localhost:80)</c>. The path assertions of the disclosure test are
    /// therefore a guard against a future message that adds it, not a control of a leak that exists
    /// today; the type names are what the control can prove the probe is able to see.
    /// </remarks>
    /// <returns>Resolves when the assertion has been made.</returns>
    [Fact]
    public async Task The_real_transport_exception_does_carry_the_type_names_the_disclosure_test_hunts()
    {
        var socket = NewSocket(UnreachableAgentSocket.MissingShape);
        using var channel = AgentChannel.CreateUnixSocket(socket.SocketPath);
        var grpc = new SystemService.SystemServiceClient(channel);

        var thrown = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await grpc.GetAgentInfoAsync(new GetAgentInfoRequest());
        });

        var text = thrown.ToString();
        Assert.Contains("HttpRequestException", text, StringComparison.Ordinal);
        Assert.Contains("SocketException", text, StringComparison.Ordinal);
    }

    /// <summary>The unavailable answer is written in the language the panel asked for.</summary>
    /// <param name="language">The <c>Accept-Language</c> the panel sends.</param>
    /// <param name="expected">That language's resx text.</param>
    [Theory]
    [MemberData(nameof(TextPerLanguage))]
    public async Task The_agent_unavailable_answer_is_written_in_the_language_the_panel_asked_for(
        string language,
        string expected)
    {
        var socket = NewSocket(UnreachableAgentSocket.MissingShape);
        await using var factory = await BootAsync(socket);
        using var client = await EndpointHarness.SignInAsync(factory, "admin");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var response = await client.GetAsync("/api/v1/monitoring/metrics");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("title").GetString());
    }

    /// <summary>
    /// A site creation refused because the agent is unreachable answers 503, creates no row, and is
    /// journalled as a failure naming the domain — the audit entry the unmapped exception used to skip.
    /// </summary>
    /// <param name="shape">The outage shape.</param>
    /// <returns>Resolves when the assertions have been made.</returns>
    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task A_site_creation_refused_by_an_unreachable_agent_answers_503_and_journals_the_failure(
        string shape)
    {
        var socket = NewSocket(shape);
        await using var factory = await BootAsync(socket);
        var accountId = await SeedAccountAsync(factory);
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync("/api/v1/sites", new
        {
            accountId,
            domain = Domain,
            aliases = Array.Empty<string>(),
            backendType = 1,
            phpVersion = string.Empty,
            proxyUpstream = string.Empty,
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AgentUnavailable", problem.RootElement.GetProperty("code").GetString());

        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        Assert.Empty(await sites.Sites.IgnoreQueryFilters().ToListAsync());

        using var journal = JsonDocument.Parse(await client.GetStringAsync("/api/v1/audit"));
        var created = journal.RootElement.EnumerateArray()
            .Where(candidate =>
            {
                return candidate.GetProperty("action").GetString() == "SiteCreated";
            })
            .ToList();
        var entry = Assert.Single(created);
        Assert.False(entry.GetProperty("succeeded").GetBoolean());
        Assert.Equal(Domain, entry.GetProperty("subject").GetString());
    }

    /// <summary>Records a socket shape for disposal and returns it.</summary>
    /// <param name="shape">The shape name.</param>
    /// <returns>The socket.</returns>
    private UnreachableAgentSocket NewSocket(string shape)
    {
        var socket = UnreachableAgentSocket.Of(shape);
        _sockets.Add(socket);

        return socket;
    }

    /// <summary>Boots the host pinned to the socket, migrates, and seeds the administrator.</summary>
    /// <param name="socket">The unreachable agent path.</param>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync(UnreachableAgentSocket socket)
    {
        var factory = EndpointHarness.CreateFactory(_pg)
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Agent:SocketPath", socket.SocketPath);
            });
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);

        return factory;
    }

    /// <summary>Seeds the account the creation is asked for.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The account's identifier.</returns>
    private static async Task<Guid> SeedAccountAsync(WebApplicationFactory<Program> factory)
    {
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "outage");

        return accountId;
    }
}
