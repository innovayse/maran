using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/sites/php-versions/install</c> over real HTTP and real PostgreSQL, with only the
/// agent replaced (issue #99).
/// </summary>
/// <remarks>
/// The defect was a 500 on a SUCCESSFUL install: the handler answered with the agent client's
/// <c>PhpVersionDto</c> while the controller asked the bus for the Sites module's own record of the
/// same name, so the bus returned nothing and the result translation threw. Nothing but the real
/// endpoint can observe that seam, which is why these tests do not call the handler.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PhpVersionInstallEndpointTests : IAsyncLifetime
{
    /// <summary>The FPM socket directory the scripted host reports; it must never reach the wire.</summary>
    private const string SocketDirectory = "/run/php/internal-secret-dir";

    /// <summary>This test's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PhpVersionInstallEndpointTests(PostgresFixture postgres)
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

    /// <summary>Installing a version that is not installed answers 200 with the list and no socket path.</summary>
    [Fact]
    public async Task Installing_a_version_that_is_not_installed_answers_ok_without_the_socket_directory()
    {
        var agent = new ScriptedAgentPhpClient(
            [new PhpVersionDto("8.3", SocketDirectory, true)],
            [
                new PhpInstallEvent(PhpInstallEventKind.Progress, 50, "downloading", "8.4", null),
                new PhpInstallEvent(PhpInstallEventKind.Installed, 100, "done", "8.4", null),
            ]);

        var (status, body, audit) = await InstallAsync(agent, "8.4");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, agent.InstallCalls);
        Assert.DoesNotContain("fpmSocketDirectory", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SocketDirectory, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(["8.3", "8.4"], document.RootElement.EnumerateArray().Select(entry =>
        {
            return entry.GetProperty("version").GetString();
        }));
        Assert.True(audit.GetProperty("succeeded").GetBoolean());
        Assert.Equal("8.4", audit.GetProperty("subject").GetString());
    }

    /// <summary>Installing a version that is already installed answers 200 and asks the agent for nothing.</summary>
    [Fact]
    public async Task Installing_a_version_that_is_already_installed_answers_ok_and_installs_nothing()
    {
        var agent = new ScriptedAgentPhpClient([new PhpVersionDto("8.3", SocketDirectory, true)], []);

        var (status, body, audit) = await InstallAsync(agent, "8.3");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, agent.InstallCalls);
        Assert.DoesNotContain(SocketDirectory, body, StringComparison.Ordinal);
        Assert.True(audit.GetProperty("succeeded").GetBoolean());
    }

    /// <summary>A failed install answers a typed problem document and is journalled as a failure.</summary>
    [Fact]
    public async Task A_failed_install_answers_a_typed_problem_and_is_journalled_as_a_failure()
    {
        var agent = new ScriptedAgentPhpClient(
            [new PhpVersionDto("8.3", SocketDirectory, true)],
            [new PhpInstallEvent(PhpInstallEventKind.Failed, 0, "fetching", "8.4", "AgentSystemFailure")]);

        var (status, body, audit) = await InstallAsync(agent, "8.4");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("AgentSystemFailure", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(SocketDirectory, body, StringComparison.Ordinal);
        Assert.False(audit.GetProperty("succeeded").GetBoolean());
        Assert.Equal("8.4", audit.GetProperty("subject").GetString());
    }

    /// <summary>Boots the host, installs one version as the administrator, and reads back the audit entry.</summary>
    /// <param name="agent">The scripted PHP client.</param>
    /// <param name="version">The version to install.</param>
    /// <returns>The install response's status and body, and the journal entry the install wrote.</returns>
    private async Task<(HttpStatusCode Status, string Body, JsonElement Audit)> InstallAsync(
        IAgentPhpClient agent,
        string version)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton(agent);
        });
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var response = await admin.PostAsJsonAsync("/api/v1/sites/php-versions/install", new { version });
        var body = await response.Content.ReadAsStringAsync();

        using var journal = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/audit"));
        var entry = journal.RootElement.EnumerateArray().Single(candidate =>
        {
            return candidate.GetProperty("action").GetString() == "PhpVersionInstalled";
        }).Clone();

        return (response.StatusCode, body, entry);
    }
}
