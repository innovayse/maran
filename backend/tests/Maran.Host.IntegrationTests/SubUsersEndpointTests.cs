using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Sftp.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// An account's owner invites, limits and removes team members over real HTTP, and the members are
/// refused what they were not given — independently of the plan and of any menu.
/// </summary>
/// <remarks>
/// <para>
/// File transfer (the SFTP logins endpoint) is the gated feature the tests probe: its controller opts
/// in with <c>[RequiresAccountFeature(AccountFeature.FileTransfer)]</c>, so a member without the grant
/// is refused by the filter before the handler runs, and a member with it reaches the handler's
/// ordinary answer. The plan is the default one, which includes every feature, so every refusal here is
/// the PERSON's and not the plan's.
/// </para>
/// <para>
/// UNOBSERVED HERE: the mail transport itself (the recording mailer stands in for the SMTP server);
/// the invitation reaches it through the shipped publish, queue and handler.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SubUsersEndpointTests : IAsyncLifetime
{
    /// <summary>The grant list naming only the databases feature.</summary>
    private static readonly string[] DatabasesFeatures = { "databases" };

    /// <summary>The grant list naming only the file transfer feature.</summary>
    private static readonly string[] FileTransferFeatures = { "fileTransfer" };

    /// <summary>The grant list naming only the backups feature.</summary>
    private static readonly string[] BackupsFeatures = { "backups" };

    /// <summary>The grant list naming only the cron feature.</summary>
    private static readonly string[] CronFeatures = { "cron" };

    /// <summary>The grant list naming a feature the panel does not have.</summary>
    private static readonly string[] UnknownFeatures = { "root" };

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The mail server's double: records what the panel mails.</summary>
    private readonly RecordingMailer _mailer = new(TimeSpan.Zero);

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SubUsersEndpointTests(PostgresFixture postgres)
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

    /// <summary>The owner invites a person, who accepts the mailed invitation, signs in, and holds exactly the chosen features.</summary>
    [Fact]
    public async Task An_owner_invites_a_person_who_accepts_and_signs_in_holding_the_chosen_features()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var invited = await owner.PostAsJsonAsync(
            "/api/v1/me/sub-users",
            new { Username = "deploy", Email = "deploy@example.com", Features = DatabasesFeatures });

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        using (var body = JsonDocument.Parse(await invited.Content.ReadAsStringAsync()))
        {
            Assert.Equal("invited", body.RootElement.GetProperty("state").GetString());
            Assert.Equal("databases", Assert.Single(body.RootElement.GetProperty("features").EnumerateArray()).GetString());
        }

        var token = await MailedTokenAsync("deploy@example.com");
        using var anonymous = factory.CreateClient();
        var accepted = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/accept-invitation", new { Token = token, NewPassword = EndpointHarness.Password });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        var profile = await member.GetAsync("/api/v1/profile");
        using var profileBody = JsonDocument.Parse(await profile.Content.ReadAsStringAsync());
        var access = profileBody.RootElement.GetProperty("accountAccess");
        Assert.Equal("member", access.GetProperty("role").GetString());
        Assert.Equal("databases", Assert.Single(access.GetProperty("features").EnumerateArray()).GetString());
    }

    /// <summary>A member is refused a feature they were not given, whatever the plan includes.</summary>
    [Fact]
    public async Task A_member_is_refused_a_feature_they_were_not_given()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", AccountFeature.Databases);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");

        var refused = await member.GetAsync("/api/v1/sftp-users");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("AccountFeatureNotGranted", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>The inverse control: the same member, granted the feature, reaches the endpoint, and so does the owner.</summary>
    [Fact]
    public async Task A_member_given_the_feature_and_the_owner_reach_it()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", AccountFeature.FileTransfer);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/sftp-users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/sftp-users")).StatusCode);
    }

    /// <summary>Changing the grants takes effect on the member's very next request, with the token they already hold.</summary>
    [Fact]
    public async Task Changing_grants_takes_effect_on_the_next_request()
    {
        await using var factory = await BootAsync();
        var memberId = await SeedMemberAsync(factory, "deploy", AccountFeature.Databases);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/sftp-users")).StatusCode);

        var changed = await owner.PutAsJsonAsync(
            $"/api/v1/me/sub-users/{memberId}/features", new { Features = FileTransferFeatures });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/sftp-users")).StatusCode);
    }

    /// <summary>Removing a member ends their access at once: the token in their hand is refused and they cannot sign in again.</summary>
    [Fact]
    public async Task Removing_a_member_ends_their_access_at_once()
    {
        await using var factory = await BootAsync();
        var memberId = await SeedMemberAsync(factory, "deploy", AccountFeature.FileTransfer);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/sftp-users")).StatusCode);

        var removed = await owner.DeleteAsync($"/api/v1/me/sub-users/{memberId}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/sftp-users")).StatusCode);
        using var anonymous = factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "deploy", Password = EndpointHarness.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await identity.Users.AnyAsync(candidate => candidate.Id == memberId));
    }

    /// <summary>Another account's owner cannot see, change or remove a sub-user: every attempt is a 404.</summary>
    [Fact]
    public async Task Another_accounts_owner_gets_404_for_every_sub_user_operation()
    {
        await using var factory = await BootAsync();
        var memberId = await SeedMemberAsync(factory, "deploy", AccountFeature.Databases);
        using var rival = await EndpointHarness.SignInAsync(factory, "owner-b");

        var change = await rival.PutAsJsonAsync(
            $"/api/v1/me/sub-users/{memberId}/features", new { Features = BackupsFeatures });
        var remove = await rival.DeleteAsync($"/api/v1/me/sub-users/{memberId}");
        var list = await rival.GetAsync("/api/v1/me/sub-users");

        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());

        // The sub-user is exactly as it was.
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        using var own = JsonDocument.Parse(await (await owner.GetAsync("/api/v1/me/sub-users")).Content.ReadAsStringAsync());
        var row = Assert.Single(own.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("databases", Assert.Single(row.GetProperty("features").EnumerateArray()).GetString());
    }

    /// <summary>A team member, an administrator and an anonymous caller cannot manage the team.</summary>
    [Fact]
    public async Task Only_the_owner_can_manage_the_team()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", AccountFeature.All);
        using var member = await EndpointHarness.SignInAsync(factory, "deploy");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var anonymous = factory.CreateClient();
        var invitation = new { Username = "friend", Email = "friend@example.com", Features = Array.Empty<string>() };

        var asMember = await member.PostAsJsonAsync("/api/v1/me/sub-users", invitation);
        var listAsMember = await member.GetAsync("/api/v1/me/sub-users");
        var asAdmin = await admin.PostAsJsonAsync("/api/v1/me/sub-users", invitation);
        var asAnonymous = await anonymous.GetAsync("/api/v1/me/sub-users");

        Assert.Equal(HttpStatusCode.Forbidden, asMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listAsMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
        using var body = JsonDocument.Parse(await asMember.Content.ReadAsStringAsync());
        Assert.Equal("SubUsersOwnerOnly", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A login name or an address that is taken is a conflict, and a feature that does not exist is a bad request.</summary>
    [Fact]
    public async Task Taken_names_conflict_and_unknown_features_are_refused()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        var takenName = await owner.PostAsJsonAsync(
            "/api/v1/me/sub-users",
            new { Username = "owner-b", Email = "fresh@example.com", Features = Array.Empty<string>() });
        var takenAddress = await owner.PostAsJsonAsync(
            "/api/v1/me/sub-users",
            new { Username = "fresh", Email = "owner-b@example.com", Features = Array.Empty<string>() });
        var unknownFeature = await owner.PostAsJsonAsync(
            "/api/v1/me/sub-users",
            new { Username = "fresh", Email = "fresh@example.com", Features = UnknownFeatures });

        Assert.Equal(HttpStatusCode.Conflict, takenName.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, takenAddress.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownFeature.StatusCode);
    }

    /// <summary>The list searches by name literally, and the features endpoint lists every feature with a label.</summary>
    [Fact]
    public async Task The_list_searches_literally_and_the_features_are_offered_with_labels()
    {
        await using var factory = await BootAsync();
        await SeedMemberAsync(factory, "deploy", AccountFeature.None);
        await SeedMemberAsync(factory, "dep_oy", AccountFeature.None);
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");

        using var wildcard = JsonDocument.Parse(
            await (await owner.GetAsync("/api/v1/me/sub-users?search=dep_oy")).Content.ReadAsStringAsync());
        using var features = JsonDocument.Parse(
            await (await owner.GetAsync("/api/v1/me/sub-users/features")).Content.ReadAsStringAsync());

        // The underscore matches itself only: "deploy" does not match "dep_oy".
        var row = Assert.Single(wildcard.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("dep_oy", row.GetProperty("username").GetString());
        Assert.Equal(6, features.RootElement.GetArrayLength());
        Assert.All(features.RootElement.EnumerateArray(), feature =>
        {
            Assert.False(string.IsNullOrWhiteSpace(feature.GetProperty("featureDisplayName").GetString()));
        });
    }

    /// <summary>Each mutation leaves one journal entry naming the owner, and a refused attempt leaves a failed one.</summary>
    [Fact]
    public async Task Every_mutation_is_audited()
    {
        await using var factory = await BootAsync();
        using var owner = await EndpointHarness.SignInAsync(factory, "owner-a");
        var invited = await owner.PostAsJsonAsync(
            "/api/v1/me/sub-users",
            new { Username = "deploy", Email = "deploy@example.com", Features = CronFeatures });
        using var body = JsonDocument.Parse(await invited.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        await owner.PutAsJsonAsync($"/api/v1/me/sub-users/{id}/features", new { Features = BackupsFeatures });
        await owner.DeleteAsync($"/api/v1/me/sub-users/{id}");
        await owner.DeleteAsync($"/api/v1/me/sub-users/{Guid.NewGuid()}");

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entries = await identity.AuditEvents.AsNoTracking()
            .Where(entry => entry.Action.StartsWith("SubUser"))
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();

        Assert.Equal(
            [AuditActions.SubUserInvited, AuditActions.SubUserGrantsChanged, AuditActions.SubUserRemoved, AuditActions.SubUserRemoved],
            entries.Select(entry => { return entry.Action; }));
        Assert.All(entries, entry => { Assert.Equal("owner-a", entry.ActorUsername); });
        Assert.Equal([true, true, true, false], entries.Select(entry => { return entry.Succeeded; }));
        Assert.Equal("deploy", entries[0].Subject);
    }

    /// <summary>Boots the panel with two accounts, an owner each, and an administrator.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(
                _pg,
                services =>
                {
                    services.AddSingleton<IMailer>(_mailer);
                })
            // A configured panel address makes the invitation carry a link with "token=", which is
            // the shape the person actually receives on an installed panel.
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("PasswordReset:PanelUrl", "https://panel.example.com");
            });
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountA) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, accountB) = await EndpointHarness.SeedAccountAsync(factory, "beta");
        await EndpointHarness.SeedCustomerAsync(factory, "owner-a", accountA);
        await EndpointHarness.SeedCustomerAsync(factory, "owner-b", accountB);

        return factory;
    }

    /// <summary>Stores an active team member of the first account with the given grants.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The member's login name.</param>
    /// <param name="grants">The features the owner gave them.</param>
    /// <returns>The member's login id.</returns>
    private static async Task<Guid> SeedMemberAsync(
        WebApplicationFactory<Program> factory,
        string username,
        AccountFeature grants)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var ownerMembership = await (
            from membership in identity.AccountMemberships
            join user in identity.Users on membership.UserId equals user.Id
            where user.Username == "owner-a"
            select membership).SingleAsync();

        var member = User.InviteMember(Guid.NewGuid(), username, $"{username}@example.com", now);
        member.Activate(hasher.Hash(EndpointHarness.Password));
        identity.Users.Add(member);
        identity.AccountMemberships.Add(
            AccountMembership.Admit(Guid.NewGuid(), member.Id, ownerMembership.AccountId, grants, now));
        await identity.SaveChangesAsync();

        return member.Id;
    }

    /// <summary>Waits for the invitation mail to <paramref name="recipient"/> and reads the token out of its link.</summary>
    /// <param name="recipient">The address the invitation was sent to.</param>
    /// <returns>The plaintext token the person received.</returns>
    private async Task<string> MailedTokenAsync(string recipient)
    {
        var waited = Stopwatch.StartNew();
        SentMail? mail = null;
        while (mail is null && waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            mail = _mailer.Sent.FirstOrDefault(candidate =>
            {
                return candidate.Recipient == recipient;
            });
            if (mail is null)
            {
                await Task.Delay(50);
            }
        }

        Assert.NotNull(mail);
        var match = Regex.Match(mail.Body, @"token=([A-Za-z0-9_\-%]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(match.Success, "The invitation mail carried no token.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }
}
