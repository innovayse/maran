using Maran.Modules.Identity.Commands.CompleteSetup;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.CompleteSetup;
/// <summary>Behavioural contract of complete setup command handler.</summary>

public sealed class CompleteSetupCommandHandlerTests : IDisposable
{
    private const string Token = "a-one-time-token";

    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static CompleteSetupCommand Command(string token = Token)
    {
        return new CompleteSetupCommand(
            token, "admin", "admin@example.com", "correct horse battery staple", "203.0.113.7", "agent");
    }

    private CompleteSetupCommandHandler NewHandler(string configuredToken = Token)
    {
        return new CompleteSetupCommandHandler(
            _context,
            new Argon2idPasswordHasher(),
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            new FakeClock(Now),
            new OptionsWrapper<SetupOptions>(new SetupOptions { Token = configuredToken }));
    }

    /// <summary>Completing setup on an empty panel creates an administrator.</summary>
    [Fact]
    public async Task Completing_setup_on_an_empty_panel_creates_an_administrator()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = await _context.Users.SingleAsync();
        Assert.Equal("admin", user.Username);
        Assert.Equal(UserRole.Admin, user.Role);
    }

    /// <summary>The created administrators password is stored only as a hash.</summary>
    [Fact]
    public async Task The_created_administrators_password_is_stored_only_as_a_hash()
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var stored = (await _context.Users.SingleAsync()).PasswordHash;
        Assert.DoesNotContain("correct horse battery staple", stored, StringComparison.Ordinal);
        Assert.StartsWith("$argon2id$", stored, StringComparison.Ordinal);
    }

    /// <summary>Completing setup with a wrong token fails and creates nobody.</summary>
    [Fact]
    public async Task Completing_setup_with_a_wrong_token_fails_and_creates_nobody()
    {
        var result = await NewHandler().HandleAsync(Command(token: "not-the-token"), CancellationToken.None);

        Assert.Equal("SetupTokenInvalidUnauthorized", result.Error!.Code);
        Assert.Empty(await _context.Users.ToListAsync());
    }

    /// <summary>Setup is refused when no token is configured at all.</summary>
    [Fact]
    public async Task Setup_is_refused_when_no_token_is_configured_at_all()
    {
        // An empty configured token must not be satisfied by an empty supplied one: that would
        // hand the panel to the first stranger to post an empty string.
        var result = await NewHandler(configuredToken: string.Empty).HandleAsync(Command(token: ""), CancellationToken.None);

        Assert.Equal("SetupTokenInvalidUnauthorized", result.Error!.Code);
    }

    /// <summary>Completing setup when a user already exists is refused even with the right token.</summary>
    [Fact]
    public async Task Completing_setup_when_a_user_already_exists_is_refused_even_with_the_right_token()
    {
        _context.Users.Add(new User(Guid.NewGuid(), "someone", "s@example.com", "hash", UserRole.Admin, Now));
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("SetupAlreadyCompletedForbidden", result.Error!.Code);
        Assert.Single(await _context.Users.ToListAsync());
    }

    /// <summary>Completing setup writes an audit event that does not contain the token or the password.</summary>
    [Fact]
    public async Task Completing_setup_writes_an_audit_event_that_does_not_contain_the_token_or_the_password()
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = _audit.Written.Single();
        Assert.Equal(AuditActions.AdministratorCreated, entry.Action);
        Assert.DoesNotContain(Token, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A refused setup writes no audit entry naming a user that was never created.</summary>
    [Fact]
    public async Task A_refused_setup_writes_no_audit_entry_naming_a_user_that_was_never_created()
    {
        await NewHandler().HandleAsync(Command(token: "not-the-token"), CancellationToken.None);

        Assert.Empty(_audit.Written);
    }
}
