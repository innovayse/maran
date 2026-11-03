using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The panel the sub-user endpoint tests run against: an administrator, two accounts each with its own
/// owner, a third customer who owns an account of their own, and the helpers the resend, offer and
/// invitee tests share.
/// </summary>
/// <remarks>
/// <para>
/// Accounts: <c>acme</c> (owner <c>owner-a</c>) and <c>beta</c> (owner <c>owner-b</c>). The customer
/// <c>pat</c> owns <c>gamma</c>, so a login that already belongs to an account elsewhere is the ordinary
/// target of an offer. Every login has the password <see cref="EndpointHarness.Password"/> and the
/// address <c>&lt;username&gt;@example.com</c>.
/// </para>
/// <para>
/// UNOBSERVED HERE: the mail transport itself. The recording mailer stands in for the SMTP server;
/// a message reaches it through the shipped publish, queue and handler.
/// </para>
/// </remarks>
public static class SubUserTeamWorld
{
    /// <summary>How long a test waits for a message to reach the recording mailer.</summary>
    private static readonly TimeSpan MailWait = TimeSpan.FromSeconds(30);

    /// <summary>Boots the panel with the standard accounts and logins described on the type.</summary>
    /// <param name="database">The test's own database.</param>
    /// <param name="mailer">The mail server's double.</param>
    /// <param name="clock">A clock the test moves, or <c>null</c> for the real one.</param>
    /// <param name="configure">Extra host settings a test needs, or <c>null</c>.</param>
    /// <returns>The booted factory, which the caller disposes.</returns>
    public static async Task<WebApplicationFactory<Program>> BootAsync(
        TestDatabase database,
        RecordingMailer mailer,
        AdvancingClock? clock = null,
        Action<IWebHostBuilder>? configure = null)
    {
        var factory = EndpointHarness.CreateFactory(
                database,
                services =>
                {
                    services.AddSingleton<IMailer>(mailer);
                    if (clock is not null)
                    {
                        services.AddSingleton<IClock>(clock);
                    }
                })
            // A configured panel address makes an invitation carry a link with "token=", which is the
            // shape the person actually receives on an installed panel.
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("PasswordReset:PanelUrl", "https://panel.example.com");
                configure?.Invoke(builder);
            });
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, acme) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, beta) = await EndpointHarness.SeedAccountAsync(factory, "beta");
        var (_, gamma) = await EndpointHarness.SeedAccountAsync(factory, "gamma");
        await EndpointHarness.SeedCustomerAsync(factory, "owner-a", acme);
        await EndpointHarness.SeedCustomerAsync(factory, "owner-b", beta);
        await EndpointHarness.SeedCustomerAsync(factory, "pat", gamma);

        return factory;
    }

    /// <summary>Stores an active customer login that belongs to no account at all.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    public static async Task SeedAccountlessCustomerAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(),
            username,
            $"{username}@example.com",
            hasher.Hash(EndpointHarness.Password),
            UserRole.Customer,
            now));
        await identity.SaveChangesAsync();
    }

    /// <summary>Stores a login that was invited and has not accepted, with no membership.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    public static async Task SeedInvitedLoginAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(User.Invite(Guid.NewGuid(), username, $"{username}@example.com", now));
        await identity.SaveChangesAsync();
    }

    /// <summary>Stores an active team member of the account <c>acme</c> with the given grants.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The member's login name.</param>
    /// <param name="grants">The features the owner gave them.</param>
    /// <returns>The member's login id.</returns>
    public static async Task<Guid> SeedMemberAsync(
        WebApplicationFactory<Program> factory,
        string username,
        AccountFeature grants)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var acme = await AccountIdAsync(factory, "acme");

        var member = User.Invite(Guid.NewGuid(), username, $"{username}@example.com", now);
        member.Activate(hasher.Hash(EndpointHarness.Password));
        identity.Users.Add(member);
        identity.AccountMemberships.Add(AccountMembership.Admit(Guid.NewGuid(), member.Id, acme, grants, now));
        await identity.SaveChangesAsync();

        return member.Id;
    }

    /// <summary>
    /// Suspends an account on both sides it lives on: the account row itself, and Identity's memberships
    /// through the real subscriber production runs when the suspend command publishes the event. The
    /// administrator's suspend endpoint is not used because it first stops what the account runs through
    /// the agent, which these tests do not stand up (the pattern <c>SuspendedAccountMemberEndpointTests</c> uses).
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's name.</param>
    public static async Task SuspendAccountAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var account = await accounts.Accounts.SingleAsync(candidate => candidate.Name == accountName);
        account.Suspend();
        await accounts.SaveChangesAsync();

        var handler = new AccountSuspendingHandler(
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            scope.ServiceProvider.GetRequiredService<ISessionService>());
        await handler.HandleAsync(new AccountSuspending(account.Id, "owner"), CancellationToken.None);
    }

    /// <summary>Reads an account's id by name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's name.</param>
    /// <returns>Its id.</returns>
    public static async Task<Guid> AccountIdAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        return await accounts.Accounts.Where(account => account.Name == accountName)
            .Select(account => account.Id)
            .SingleAsync();
    }

    /// <summary>Reads a login's id by name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    /// <returns>The login's id.</returns>
    public static async Task<Guid> UserIdAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.Users.AsNoTracking()
            .Where(candidate => candidate.Username == username)
            .Select(candidate => candidate.Id)
            .SingleAsync();
    }

    /// <summary>Counts the membership offers of one login for one account that are still open.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The offered login.</param>
    /// <param name="accountName">The offering account.</param>
    /// <returns>How many offers have not been accepted, declined or cancelled.</returns>
    public static async Task<int> OpenOfferCountAsync(
        WebApplicationFactory<Program> factory,
        string username,
        string accountName)
    {
        var userId = await UserIdAsync(factory, username);
        var accountId = await AccountIdAsync(factory, accountName);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.AccountMembershipOffers.IgnoreQueryFilters()
            .CountAsync(offer => offer.UserId == userId && offer.AccountId == accountId && offer.ResolvedAt == null);
    }

    /// <summary>Counts the memberships a login holds of one account, in any state.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login.</param>
    /// <param name="accountName">The account.</param>
    /// <returns>How many membership rows exist.</returns>
    public static async Task<int> MembershipCountAsync(
        WebApplicationFactory<Program> factory,
        string username,
        string accountName)
    {
        var userId = await UserIdAsync(factory, username);
        var accountId = await AccountIdAsync(factory, accountName);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.AccountMemberships.IgnoreQueryFilters()
            .CountAsync(membership => membership.UserId == userId && membership.AccountId == accountId);
    }

    /// <summary>Reads every journal entry whose action is one of <paramref name="actions"/>, oldest first.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="actions">The action names to keep.</param>
    /// <returns>The matching entries.</returns>
    public static async Task<IReadOnlyList<AuditEvent>> AuditEntriesAsync(
        WebApplicationFactory<Program> factory,
        params string[] actions)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.AuditEvents.AsNoTracking()
            .Where(entry => actions.Contains(entry.Action))
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();
    }

    /// <summary>Starts viewing the panel as the owner of an account and returns a client carrying that token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account to view as.</param>
    /// <returns>A client the caller disposes.</returns>
    public static async Task<HttpClient> StartViewingAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var started = await admin.PostAsJsonAsync(
            "/api/v1/view-as", new { AccountId = await AccountIdAsync(factory, accountName) });
        started.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var viewing = factory.CreateClient();
        viewing.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("accessToken").GetString()!);
        return viewing;
    }

    /// <summary>Waits for a message to <paramref name="recipient"/> to reach the mailer.</summary>
    /// <param name="mailer">The mail server's double.</param>
    /// <param name="recipient">The address the message was sent to.</param>
    /// <param name="skip">How many earlier messages to that address to ignore.</param>
    /// <returns>The message.</returns>
    public static async Task<SentMail> MailToAsync(RecordingMailer mailer, string recipient, int skip = 0)
    {
        var waited = Stopwatch.StartNew();
        SentMail? mail = null;
        while (mail is null && waited.Elapsed < MailWait)
        {
            mail = mailer.Sent.Where(candidate => { return candidate.Recipient == recipient; })
                .Skip(skip)
                .FirstOrDefault();
            if (mail is null)
            {
                await Task.Delay(50);
            }
        }

        Assert.NotNull(mail);
        return mail;
    }

    /// <summary>Reads the plaintext token out of the link in an invitation mail.</summary>
    /// <param name="mail">The invitation mail.</param>
    /// <returns>The token the person received.</returns>
    public static string TokenIn(SentMail mail)
    {
        var match = Regex.Match(mail.Body, @"token=([A-Za-z0-9_\-%]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(match.Success, "The invitation mail carried no token.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }
}
