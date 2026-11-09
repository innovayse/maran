using Maran.Host.Cli;
using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Models;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Cli;

/// <summary>
/// <c>Maran.Host --cli report-update</c> against a real database: a failed unattended update lands in
/// the audit journal as failed, with the operator alert beside it, and a successful one is journalled
/// without an alert (issue #383).
/// </summary>
/// <remarks>
/// The verb is driven through the composed container's <see cref="CliRunner"/>, exactly as
/// <c>Program</c> does. No alert address is configured, so the mail is skipped and the journal says so,
/// which is the ordinary state of a fresh installation.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ReportUpdateCliTests : IAsyncLifetime
{
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string Verb = "report-update";

    private readonly TestDatabase _pg;

    /// <summary>The unconfigured factories each derived factory was built from, released with the test.</summary>
    private readonly List<WebApplicationFactory<Program>> _roots = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ReportUpdateCliTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares a fresh database before each test.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases the root factories; the shared server owns the databases.</summary>
    public async Task DisposeAsync()
    {
        foreach (var root in _roots)
        {
            await root.DisposeAsync();
        }
    }

    /// <summary>Boots the real panel against the test database and creates the schemas the verb writes to.</summary>
    /// <returns>The configured factory.</returns>
    private async Task<WebApplicationFactory<Program>> CreateFactoryAsync()
    {
        var root = new WebApplicationFactory<Program>();
        _roots.Add(root);
        var factory = root.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
            builder.UseSetting("Panel:PublicUrl", "https://panel.example.com");

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
        return factory;
    }

    /// <summary>Runs the verb through the composed container, as <c>Program</c> does.</summary>
    /// <param name="factory">The booted panel.</param>
    /// <param name="arguments">The verb's arguments.</param>
    /// <returns>The exit status and what the verb wrote to standard output.</returns>
    private static async Task<(int Status, string Output)> RunVerbAsync(
        WebApplicationFactory<Program> factory,
        params string[] arguments)
    {
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = factory.Services.GetRequiredService<CliRunner>();

        var status = await runner.RunAsync(
            new CliInvocation(Verb, arguments, false, false, null),
            new CliConsole(input, output, error),
            CancellationToken.None);

        return (status, output.ToString());
    }

    /// <summary>Reads every audit action the journal holds with its outcome, oldest first.</summary>
    /// <param name="factory">The booted panel.</param>
    /// <returns>The action, subject and whether it succeeded.</returns>
    private static async Task<List<(string Action, string Subject, bool Succeeded)>> JournalAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var rows = await identity.AuditEvents.AsNoTracking().OrderBy(entry => entry.OccurredAt).ToListAsync();
        return rows.Select(row =>
        {
            return (row.Action, row.Subject, row.Succeeded);
        }).ToList();
    }

    /// <summary>A rolled-back update is journalled as failed, with the operator alert beside it.</summary>
    [Fact]
    public async Task A_rolled_back_update_is_journalled_as_failed_and_raises_an_alert()
    {
        await using var factory = await CreateFactoryAsync();

        var run = await RunVerbAsync(factory, "rolled-back", "1.2.3", "1.2.4");

        Assert.Equal((int)CliExitCode.Success, run.Status);
        Assert.Equal(string.Empty, run.Output);
        var journal = await JournalAsync(factory);
        Assert.Contains(journal, row =>
        {
            return row.Action == MonitoringAuditActions.PanelUpdateRolledBack
                && row.Subject == "1.2.3->1.2.4"
                && !row.Succeeded;
        });
        Assert.Contains(journal, row =>
        {
            return row.Action == MonitoringAuditActions.AlertRaised && row.Subject.StartsWith("PanelUpdate:", StringComparison.Ordinal);
        });
    }

    /// <summary>An applied update is journalled as succeeded and raises no alert (the inverse control).</summary>
    [Fact]
    public async Task An_applied_update_is_journalled_as_succeeded_and_raises_no_alert()
    {
        await using var factory = await CreateFactoryAsync();

        var run = await RunVerbAsync(factory, "applied", "1.2.3", "1.2.4");

        Assert.Equal((int)CliExitCode.Success, run.Status);
        var journal = await JournalAsync(factory);
        Assert.Contains(journal, row =>
        {
            return row.Action == MonitoringAuditActions.PanelUpdateApplied && row.Succeeded;
        });
        Assert.DoesNotContain(journal, row =>
        {
            return row.Action == MonitoringAuditActions.AlertRaised;
        });
    }

    /// <summary>A hostile release name is refused and nothing is journalled.</summary>
    [Fact]
    public async Task A_hostile_release_name_is_refused_and_nothing_is_journalled()
    {
        await using var factory = await CreateFactoryAsync();

        var run = await RunVerbAsync(factory, "rolled-back", "1.2.3", "../../etc/passwd");

        Assert.Equal((int)CliExitCode.Usage, run.Status);
        Assert.Empty(await JournalAsync(factory));
    }
}
