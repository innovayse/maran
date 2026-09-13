using System.Text;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;
/// <summary>Behavioural contract of database audit writer.</summary>

public sealed class DatabaseAuditWriterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private DatabaseAuditWriter NewWriter()
    {
        return new DatabaseAuditWriter(_context, new FakeClock(Now), new FakeCorrelationIdAccessor("correlation-1"));
    }

    /// <summary>A written entry is stamped with the clock and the correlation id.</summary>
    [Fact]
    public async Task A_written_entry_is_stamped_with_the_clock_and_the_correlation_id()
    {
        await NewWriter().WriteAsync(
            new AuditEntry(Guid.NewGuid(), "admin", AuditActions.LoginSucceeded, "admin", "203.0.113.7", "agent", Succeeded: true),
            CancellationToken.None);

        var stored = await _context.AuditEvents.SingleAsync();
        Assert.Equal(Now, stored.OccurredAt);
        Assert.Equal("correlation-1", stored.CorrelationId);
    }

    /// <summary>A failed login is recorded with no actor but with the attempted username.</summary>
    [Fact]
    public async Task A_failed_login_is_recorded_with_no_actor_but_with_the_attempted_username()
    {
        await NewWriter().WriteAsync(
            new AuditEntry(null, "nosuchuser", AuditActions.LoginFailed, "nosuchuser", "203.0.113.7", "agent", Succeeded: false),
            CancellationToken.None);

        var stored = await _context.AuditEvents.SingleAsync();
        Assert.Null(stored.ActorUserId);
        Assert.Equal("nosuchuser", stored.ActorUsername);
        Assert.False(stored.Succeeded);
    }

    /// <summary>An entry written outside a request carries no correlation id rather than failing.</summary>
    [Fact]
    public async Task An_entry_written_outside_a_request_carries_no_correlation_id_rather_than_failing()
    {
        var writer = new DatabaseAuditWriter(_context, new FakeClock(Now), new FakeCorrelationIdAccessor(null));

        await writer.WriteAsync(
            new AuditEntry(null, "system", AuditActions.LoginFailed, "system", "203.0.113.7", "agent", Succeeded: false),
            CancellationToken.None);

        Assert.Null((await _context.AuditEvents.SingleAsync()).CorrelationId);
    }

    /// <summary>A claimed name too long for its column is stored short rather than losing the row.</summary>
    /// <remarks>
    /// The anonymous password-reset endpoint journals the address it was given, and the shared
    /// address rule allows the standard's 320 characters while <c>ActorUsername</c> is
    /// <c>character varying(64)</c>. Unfitted, the INSERT raises 22001 and the row recording the
    /// request is the row that is lost — which is the only place a sweep through guessed addresses
    /// is visible. The assertion is the exact width, not merely "not longer than": a fit that cut
    /// to a third of the column would satisfy a bound and throw the evidence away.
    /// </remarks>
    [Fact]
    public async Task A_claimed_name_too_long_for_its_column_is_stored_short_rather_than_losing_the_row()
    {
        var claimed = new string('a', 300) + "@example.com";

        await NewWriter().WriteAsync(
            new AuditEntry(null, claimed, AuditActions.PasswordResetRequested, claimed, "203.0.113.7", "agent", Succeeded: true),
            CancellationToken.None);

        var stored = await _context.AuditEvents.SingleAsync();
        Assert.Equal(AuditEvent.ActorUsernameMaxLength, stored.ActorUsername.Length);
        Assert.Equal(claimed[..AuditEvent.ActorUsernameMaxLength], stored.ActorUsername);
        Assert.Equal(AuditEvent.SubjectMaxLength, stored.Subject.Length);
        Assert.Equal(claimed[..AuditEvent.SubjectMaxLength], stored.Subject);
    }

    /// <summary>A subject cut at its column's width is never left holding half a character.</summary>
    /// <remarks>
    /// A refused firewall ban journals the malformed address it was asked for, verbatim and with no
    /// length rule in front of it, so the caller chooses both the length and the characters. A cut
    /// at a fixed UTF-16 index in a subject of non-BMP characters leaves a lone surrogate, which
    /// Npgsql's write buffer throws on rather than substituting — the same lost row by a second
    /// route. The subject here is one ASCII character short of an even boundary on purpose.
    /// </remarks>
    [Fact]
    public async Task A_subject_cut_at_its_column_width_is_never_left_holding_half_a_character()
    {
        var subject = "!" + string.Concat(Enumerable.Repeat("\U0001F600", 400));

        await NewWriter().WriteAsync(
            new AuditEntry(null, "admin", AuditActions.AddressBanned, subject, "203.0.113.7", "agent", Succeeded: false),
            CancellationToken.None);

        var stored = await _context.AuditEvents.SingleAsync();
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        Assert.Equal(subject[..(1 + ((AuditEvent.SubjectMaxLength - 1) * 2))], stored.Subject);
        Assert.Equal(stored.Subject, strict.GetString(strict.GetBytes(stored.Subject)));
    }
}
