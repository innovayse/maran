using System.Diagnostics;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// R11's decoupling, measured: publishing <see cref="SendMailRequested"/> returns in milliseconds
/// even when sending takes five seconds, and it returns in milliseconds for every recipient alike.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is the test that matters.</b> The design R11 replaced sent the mail inline. That made
/// a password reset for a KNOWN address cost a full SMTP round trip while an unknown one returned
/// instantly — a seconds-scale account-enumeration oracle readable with a stopwatch, on the one
/// endpoint that is deliberately answered identically either way. Decoupling the send is what makes
/// the two paths' timing indistinguishable; this measures the decoupling itself, at the seam the
/// publisher uses.
/// </para>
/// <para>
/// <b>What this proves and what it does not.</b> It proves the panel's mail seam does not block its
/// publisher, for two different recipients, with a mailer that takes five seconds — which is the half
/// of the guarantee this module owns. The other half is the reset endpoint's own equality of status
/// and body for a known and an unknown address, which belongs to the Identity module's task, because
/// that endpoint does not exist here yet. Fine-grained timing equality is SHAPED by this design, not
/// proven by it: both paths do the same publish, and neither waits on SMTP.
/// </para>
/// <para>
/// <b>The queue is local and non-durable, which is the other half of R11.</b> The message body can
/// carry a live reset token, so its envelope must never rest on disk. Nothing here configures a
/// durable endpoint for it, and the handler catches every failure rather than throwing — a thrown
/// handler is what hands the envelope to the dead-letter store this design exists to avoid.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MailQueueDecouplingTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>
    /// The ceiling a publish must stay under. Generous against <see cref="SlowMailer.SendDuration"/>'s
    /// five seconds — the failure this catches is a two-order-of-magnitude one, so a tight bound would
    /// only buy flakiness on a loaded machine.
    /// </summary>
    private static readonly TimeSpan PublishCeiling = TimeSpan.FromSeconds(1);

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MailQueueDecouplingTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Publishing a mail request answers in milliseconds while the send takes five seconds.</summary>
    [Fact]
    public async Task Publishing_a_mail_request_answers_in_milliseconds_while_the_send_takes_five_seconds()
    {
        var mailer = new SlowMailer();
        await using var factory = CreateFactory(mailer);
        await MigrateAsync(factory);

        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var stopwatch = Stopwatch.StartNew();
        await bus.PublishAsync(new SendMailRequested("known@example.com", "Reset", "token=SECRET"));
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < PublishCeiling,
            $"Publishing took {stopwatch.Elapsed}, which means the caller waited on the mail server. "
            + "An inline send is exactly the enumeration oracle R11 exists to close.");

        // And it really was delivered, not merely dropped: a publish that went nowhere would also be
        // fast, and would pass a timing assertion while sending no mail at all.
        await mailer.Entered.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains("known@example.com", mailer.Recipients);
    }

    /// <summary>A known and an unknown recipient are published identically fast, which is the equality that closes the oracle.</summary>
    /// <remarks>
    /// The publisher does the same work for both, so both return before the mail server has been
    /// spoken to at all. The reset endpoint's own equality of status and body is the Identity module's
    /// to prove; what is proved here is that no timing difference can come from the mail path.
    /// </remarks>
    [Fact]
    public async Task A_known_and_an_unknown_recipient_are_published_identically_fast()
    {
        var mailer = new SlowMailer();
        await using var factory = CreateFactory(mailer);
        await MigrateAsync(factory);

        using var scope = factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var known = Stopwatch.StartNew();
        await bus.PublishAsync(new SendMailRequested("known@example.com", "Reset", "token=SECRET"));
        known.Stop();

        var unknown = Stopwatch.StartNew();
        await bus.PublishAsync(new SendMailRequested("unknown@example.com", "Reset", "token=SECRET"));
        unknown.Stop();

        Assert.True(known.Elapsed < PublishCeiling, $"The known-address publish took {known.Elapsed}.");
        Assert.True(unknown.Elapsed < PublishCeiling, $"The unknown-address publish took {unknown.Elapsed}.");

        // And both publishes actually reached the mailer. Two stopwatch ceilings on their own are
        // satisfied by a publish that went nowhere at all — drop the handler from discovery and the
        // timings only get better — so the equality being measured has to be an equality between two
        // sends that happened.
        await mailer.Entered.WaitAsync(TimeSpan.FromSeconds(30));
        await WaitForBothRecipientsAsync(mailer);
        Assert.Contains("known@example.com", mailer.Recipients);
        Assert.Contains("unknown@example.com", mailer.Recipients);
    }

    /// <summary>Waits until both publishes have been entered by the mailer.</summary>
    /// <param name="mailer">The mailer double both sends reach.</param>
    /// <returns>Resolves once two recipients have been recorded, or fails the test on timeout.</returns>
    /// <remarks>
    /// <see cref="SlowMailer.Entered"/> completes on the FIRST send only, and each send then holds
    /// its worker for five seconds, so the second recipient is recorded later than the first. This
    /// polls rather than sleeps (rules/testing.md "Determinism"), and its ceiling is generous enough
    /// that only a send which never happens can reach it.
    /// </remarks>
    private static async Task WaitForBothRecipientsAsync(SlowMailer mailer)
    {
        var waited = Stopwatch.StartNew();

        while (waited.Elapsed < TimeSpan.FromSeconds(60))
        {
            lock (mailer.Recipients)
            {
                if (mailer.Recipients.Count >= 2)
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Fail(
            "Only one of the two publishes ever reached the mailer, so the timings above compared a "
            + "send against a publish that went nowhere.");
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent and the mailer replaced.</summary>
    /// <param name="mailer">The mailer double every send reaches.</param>
    /// <returns>The booted host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(IMailer mailer)
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
                services.AddSingleton<Maran.Agent.Client.Interfaces.IAgentMonitorClient>(
                    new StubAgentMonitorClient());
                services.AddScoped(_ =>
                {
                    return mailer;
                });
            });
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }
}
