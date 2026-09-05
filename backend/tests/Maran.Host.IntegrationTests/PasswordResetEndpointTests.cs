using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;
using Wolverine.Persistence.Durability;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The forgot- and reset-password endpoints over real HTTP against a real database, with the mail
/// server replaced by a double the test controls.
/// </summary>
/// <remarks>
/// <para>
/// This level is the only one that can answer the question the feature exists for: not "does the
/// reset work" but "can a stranger tell, from the outside, whether an address belongs to an
/// account". Status, body and elapsed time are all observable from a browser, and all three are
/// asserted equal here.
/// </para>
/// <para>
/// The timing test is the one that pins R11. With the mailer taking five seconds, an inline send —
/// the shape an earlier draft used — would make the known-address request take five seconds and the
/// unknown-address one return immediately. Both are asserted to answer well inside a second, and the
/// mailer is separately observed to have been ENTERED afterwards, because a message that reached no
/// handler at all would also be fast and would pass a timing assertion while meaning the opposite.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PasswordResetEndpointTests : IAsyncLifetime
{
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string KnownEmail = "admin@example.com";
    private const string UnknownEmail = "nobody@example.com";
    private const string OldPassword = "the old password is long";
    private const string NewPassword = "correct horse battery staple";

    /// <summary>The ceiling a request must answer inside, against a mailer that takes five seconds.</summary>
    /// <remarks>
    /// A whole second, deliberately loose. The point is not that the endpoint is fast; it is that it
    /// does not wait for SMTP. Five seconds against one second is a five-fold margin no amount of
    /// test-machine noise closes, and a tight bound would make this a flaky test about scheduling
    /// rather than a firm one about a dependency.
    /// </remarks>
    private static readonly TimeSpan AnswerCeiling = TimeSpan.FromSeconds(1);

    /// <summary>How long the faked mail server takes, as the plan specifies.</summary>
    private static readonly TimeSpan MailerDelay = TimeSpan.FromSeconds(5);

    private readonly TestDatabase _pg;
    private readonly RecordingMailer _mailer = new(MailerDelay);

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PasswordResetEndpointTests(PostgresFixture postgres)
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

    /// <summary>Boots a panel whose mail server is the test's double.</summary>
    /// <param name="passwordResetLimit">
    /// Requests the reset limiter allows per window. Raised well above the production default for
    /// every test but the one that exercises the limiter itself: these tests make several requests
    /// from one address on purpose, and a 429 in the middle of a round-trip test would be a failure
    /// about the limiter rather than about the thing under test.
    /// </param>
    /// <returns>The configured factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(int passwordResetLimit = 50)
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
            builder.UseSetting("PasswordReset:PanelUrl", "https://panel.example.com");
            builder.UseSetting(
                "RateLimiting:PasswordResetMaxRequests",
                passwordResetLimit.ToString(CultureInfo.InvariantCulture));

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            // The ONLY substitution: the mail server is somebody else's infrastructure and cannot be
            // present. Everything the panel itself does — the publish, the queue, the handler —
            // stays the shipped code, which is what makes the timing assertion mean anything.
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IMailer>(_mailer);
            });
        });
    }

    private static async Task<Guid> SeedAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var user = new User(
            Guid.NewGuid(), "admin", KnownEmail, hasher.Hash(OldPassword), UserRole.Admin, clock.UtcNow);
        identity.Users.Add(user);
        await identity.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<(HttpStatusCode Status, string Body, TimeSpan Elapsed)> ForgotAsync(
        HttpClient client,
        string email)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { Email = email });
        var body = await response.Content.ReadAsStringAsync();
        stopwatch.Stop();

        return (response.StatusCode, body, stopwatch.Elapsed);
    }

    /// <summary>Strips the per-request correlation id, which is not part of what an answer says.</summary>
    /// <param name="body">The response body.</param>
    /// <returns>The body with the correlation id replaced by a fixed marker.</returns>
    /// <remarks>
    /// Every RFC 7807 problem the panel returns carries a fresh correlation id, so two refusals are
    /// never byte-identical and never could be. It is a value the caller was handed to quote back to
    /// an operator, not information about the account: comparing bodies with it removed is comparing
    /// what the answer actually says.
    /// </remarks>
    private static string WithoutCorrelationId(string body)
    {
        return Regex.Replace(
            body,
            "\"correlationId\":\"[^\"]*\"",
            "\"correlationId\":\"-\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
    }

    private static string TokenFrom(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-]+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(match.Success, "The reset mail carried no token.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    /// <summary>A known and an unknown address get the same status and the same body.</summary>
    [Fact]
    public async Task A_known_and_an_unknown_address_get_the_same_status_and_the_same_body()
    {
        await using var factory = CreateFactory();
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        var known = await ForgotAsync(client, KnownEmail);
        var unknown = await ForgotAsync(client, UnknownEmail);

        Assert.Equal(HttpStatusCode.OK, known.Status);
        Assert.Equal(known.Status, unknown.Status);
        Assert.Equal(WithoutCorrelationId(known.Body), WithoutCorrelationId(unknown.Body));
    }

    /// <summary>Both a known and an unknown address answer in milliseconds against a five second mailer.</summary>
    /// <remarks>
    /// The named test R11 exists for. An inline send would make the known-address request take the
    /// mailer's full five seconds while the unknown one returned at once — a seconds-scale
    /// enumeration oracle, worse than the microsecond one it would have been meant to fix.
    /// </remarks>
    [Fact]
    public async Task Both_a_known_and_an_unknown_address_answer_in_milliseconds_against_a_five_second_mailer()
    {
        await using var factory = CreateFactory();
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        // One warm-up request first, against the KNOWN address, and measured only after it. The
        // FIRST request into a freshly booted host pays for Wolverine's handler code generation,
        // EF's model build and the JIT, which is over a second of one-time cost that has nothing to
        // do with whether a mail is sent inline. The warm-up uses the known address deliberately: it
        // is the path that publishes, so warming with an unknown one would leave the publish path
        // cold and charge the measured known request for code generation — which showed up as a
        // 190 ms gap that looked like an oracle and was start-up cost.
        await ForgotAsync(client, KnownEmail);
        await _mailer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var known = await ForgotAsync(client, KnownEmail);
        var unknown = await ForgotAsync(client, UnknownEmail);

        Assert.True(
            known.Elapsed < AnswerCeiling,
            string.Format(
                CultureInfo.InvariantCulture,
                "A known address waited {0} ms; the mailer takes {1} ms, so the send is inline.",
                known.Elapsed.TotalMilliseconds,
                MailerDelay.TotalMilliseconds));
        Assert.True(
            unknown.Elapsed < AnswerCeiling,
            string.Format(
                CultureInfo.InvariantCulture,
                "An unknown address waited {0} ms.",
                unknown.Elapsed.TotalMilliseconds));

        // The publish went somewhere. Without this, a message routed to nothing at all would also be
        // fast and would pass the two assertions above while meaning the opposite of what they claim.
        // Two mails: the warm-up's and the measured known request's — so the request that was TIMED
        // is one whose mail actually reached the mailer, after it had answered.
        var waited = Stopwatch.StartNew();
        while (_mailer.Sent.Count < 2 && waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(50);
        }

        Assert.True(_mailer.Sent.Count >= 2, "The measured request published no mail.");
    }

    /// <summary>A reset link changes the password ends every session and cannot be used twice.</summary>
    [Fact]
    public async Task A_reset_link_changes_the_password_ends_every_session_and_cannot_be_used_twice()
    {
        await using var factory = CreateFactory();
        var userId = await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        var signedIn = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password = OldPassword });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        await ForgotAsync(client, KnownEmail);
        await _mailer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var token = TokenFrom(_mailer.Sent[0].Body);

        var reset = await client.PostAsJsonAsync(
            "/api/v1/auth/reset-password", new { Token = token, NewPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var withOld = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password = OldPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);

        var withNew = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var beforeTheReset = await identity.Sessions
                .Where(session => session.UserId == userId && session.RevokedAt == null)
                .CountAsync();

            // Exactly one live session: the sign-in that used the NEW password, a moment ago. The one
            // that existed before the reset is gone, which is the whole point — a stolen refresh
            // cookie must not outlive the password it was obtained under.
            Assert.Equal(1, beforeTheReset);
        }

        var replay = await client.PostAsJsonAsync(
            "/api/v1/auth/reset-password", new { Token = token, NewPassword = "another password entirely" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    /// <summary>A spent token an expired one and a token nobody issued are refused identically.</summary>
    /// <remarks>
    /// The refusal must not say which of the three it was: "already used" tells the account's owner,
    /// through the attacker who used it, that a live link existed — and "no such token" tells a
    /// guesser when they have found a real one.
    /// </remarks>
    [Fact]
    public async Task A_spent_token_an_expired_one_and_a_token_nobody_issued_are_refused_identically()
    {
        await using var factory = CreateFactory();
        var userId = await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        await ForgotAsync(client, KnownEmail);
        await _mailer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var spent = TokenFrom(_mailer.Sent[0].Body);
        await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { Token = spent, NewPassword });

        var expiredToken = Guid.NewGuid().ToString("N");
        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            identity.PasswordResetTokens.Add(new PasswordResetToken(
                Guid.NewGuid(),
                userId,
                Maran.SharedKernel.Utilities.Tokens.PasswordResetTokenHasher.Hash(expiredToken),
                clock.UtcNow - PasswordResetToken.Lifetime - TimeSpan.FromMinutes(1)));
            await identity.SaveChangesAsync();
        }

        var refusals = new List<(HttpStatusCode Status, string Body)>();
        foreach (var candidate in new[] { spent, expiredToken, "a-token-nobody-ever-issued" })
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/reset-password", new { Token = candidate, NewPassword });
            refusals.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        Assert.Equal(HttpStatusCode.BadRequest, refusals[0].Status);
        Assert.All(refusals, refusal =>
        {
            Assert.Equal(refusals[0].Status, refusal.Status);
            Assert.Equal(WithoutCorrelationId(refusals[0].Body), WithoutCorrelationId(refusal.Body));
        });
    }

    /// <summary>The token bearing envelope is never written to the message store.</summary>
    /// <remarks>
    /// <para>
    /// The at-rest half of R11, and the reason the mail travels on a LOCAL, NON-DURABLE queue. The
    /// panel persists messages in PostgreSQL (<c>PersistMessagesWithPostgresql</c>), so the claim
    /// that this particular message is not persisted is a claim about a default — and a default is
    /// exactly the kind of thing that changes under a library upgrade without anybody noticing. This
    /// asserts it instead: after a reset has been requested and the mail has actually been handed to
    /// the mailer, every table in the <c>wolverine</c> schema is searched for the token, in the text
    /// rendering of the row AND byte-wise in every <c>bytea</c> column.
    /// </para>
    /// <para>
    /// <b>The byte-wise half is what makes the search able to see anything.</b> An earlier version
    /// searched <c>to_jsonb(row)::text</c> only. Every envelope column that would carry a token —
    /// <c>body</c> in the incoming, outgoing, dead-letter and control-queue tables — is
    /// <c>bytea</c>, which <c>to_jsonb</c> renders as <c>\x…</c> hex, so a base64url needle could
    /// never match it: the test was structurally incapable of failing for the reason it exists.
    /// </para>
    /// <para>
    /// <b>Two guards, on two different axes.</b> <c>Assert.NotEmpty(tables)</c> proves the search
    /// looked somewhere; it cannot prove the search could find anything. So a positive control
    /// publishes one deliberately durable message carrying a needle of its own and requires the same
    /// probe to FIND it. Without that, replacing one blind query with another would look identical
    /// from the outside.
    /// </para>
    /// <para>
    /// What is at stake if it ever fails: a live reset token — permission to become the account —
    /// resting in a table, surviving a database dump, and outliving its own hour.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_token_bearing_envelope_is_never_written_to_the_message_store()
    {
        await using var factory = CreateFactory();
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        await ForgotAsync(client, KnownEmail);
        await _mailer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var token = TokenFrom(_mailer.Sent[0].Body);

        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();

        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "select table_name from information_schema.tables where table_schema = 'wolverine'", connection))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        // The schema must exist and hold its envelope tables. If it did not, this test would pass by
        // searching nothing — the shape of false confidence rules/testing.md names.
        Assert.NotEmpty(tables);

        foreach (var table in tables)
        {
            Assert.Equal(0L, await CountRowsContainingAsync(connection, table, token));
        }

        // The positive control: one envelope deliberately handed to the durable store, carrying a
        // needle of its own, so the zero counts above are shown to come from an absence rather than
        // from a blind query. It goes in through Wolverine's own store API and lands in the same
        // bytea body column a persisted reset mail would.
        //
        // Why not a publish. A publish cannot be made durable from outside the host's messaging
        // configuration, and one scheduled an hour out was tried first: it is NOT persisted, because
        // the local queue is buffered — which is the very property this test exists to defend. So a
        // publish-based control would assert the opposite of the thing under test.
        var clock = factory.Services.GetRequiredService<IClock>();
        var control = "control-needle-" + Guid.NewGuid().ToString("N");
        var stored = new Envelope(new SendMailRequested("control@example.com", "control", control))
        {
            Id = Guid.NewGuid(),
            MessageType = "maran.at-rest-probe-control",
            ContentType = "application/json",
            Destination = new Uri("local://at-rest-probe-control"),
            Data = System.Text.Encoding.UTF8.GetBytes($"{{\"body\":\"{control}\"}}"),
            Status = EnvelopeStatus.Scheduled,
            OwnerId = 0,
            ScheduledTime = clock.UtcNow.AddHours(1),
        };

        await factory.Services.GetRequiredService<IMessageStore>().Inbox.StoreIncomingAsync(stored);

        var controlSightings = 0L;
        foreach (var table in tables)
        {
            controlSightings += await CountRowsContainingAsync(connection, table, control);
        }

        Assert.True(
            controlSightings > 0,
            "The probe found no trace of a deliberately persisted message, so its zero counts above "
            + "prove nothing: the search cannot see a message even when one is there.");
    }

    /// <summary>Counts rows of one wolverine table whose text rendering or raw bytes carry a needle.</summary>
    /// <param name="connection">An open connection to the test's own database.</param>
    /// <param name="table">A table name read from <c>information_schema</c>, never from input.</param>
    /// <param name="needle">The string to look for.</param>
    /// <returns>How many rows carry it.</returns>
    /// <remarks>
    /// Two searches per row, because the two kinds of column need different ones. Text and json
    /// columns (<c>message_type</c>, headers) render into <c>to_jsonb(row)::text</c>; <c>bytea</c>
    /// columns render there as hex and must be searched as bytes, which is where a serialised
    /// envelope body — the only place a live token could rest — actually lives.
    /// </remarks>
    private static async Task<long> CountRowsContainingAsync(
        NpgsqlConnection connection,
        string table,
        string needle)
    {
        var byteColumns = new List<string>();
        await using (var columns = new NpgsqlCommand(
            "select column_name from information_schema.columns "
            + "where table_schema = 'wolverine' and table_name = @table and data_type = 'bytea'",
            connection))
        {
            columns.Parameters.AddWithValue("table", table);
            await using var reader = await columns.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                byteColumns.Add(reader.GetString(0));
            }
        }

        var byteProbes = string.Concat(byteColumns.Select(column =>
        {
            // raw-sql: the column name comes from information_schema on the test's own database and
            // an identifier cannot be a parameter. The searched value IS a parameter.
            return $" or position(@needle_bytes in coalesce(t.\"{column}\", ''::bytea)) > 0";
        }));

        // raw-sql: as above for the table name.
        await using var probe = new NpgsqlCommand(
            $"select count(*) from wolverine.\"{table}\" t "
            + $"where to_jsonb(t)::text like @needle_text{byteProbes}",
            connection);
        probe.Parameters.AddWithValue("needle_text", "%" + needle + "%");
        probe.Parameters.AddWithValue("needle_bytes", System.Text.Encoding.UTF8.GetBytes(needle));

        return (long)(await probe.ExecuteScalarAsync())!;
    }

    /// <summary>Reset requests beyond the configured budget are refused.</summary>
    /// <remarks>
    /// An unlimited reset endpoint is a mail bomb with the operator's own return address on it: a
    /// caller names any address they like and the panel sends to it as fast as the loop runs. The
    /// mutation that removes the rate-limit attribute must turn this red.
    /// </remarks>
    [Fact]
    public async Task Reset_requests_beyond_the_configured_budget_are_refused()
    {
        await using var factory = CreateFactory(passwordResetLimit: 2);
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        await ForgotAsync(client, KnownEmail);
        await ForgotAsync(client, KnownEmail);
        var third = await ForgotAsync(client, KnownEmail);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.Status);
    }
}
