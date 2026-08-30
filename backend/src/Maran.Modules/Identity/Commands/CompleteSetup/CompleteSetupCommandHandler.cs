using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Common.Options;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Commands.CompleteSetup;

/// <summary>Handles <see cref="CompleteSetupCommand"/> by creating the panel's first administrator.</summary>
public sealed class CompleteSetupCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Hashes the chosen password.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Records the creation.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The configured one-time token.</summary>
    private readonly string _configuredToken;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Hashes the chosen password.</param>
    /// <param name="auditWriter">Records the creation.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="setupOptions">The bound <see cref="SetupOptions"/>, carrying the installer's token.</param>
    public CompleteSetupCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IAuditWriter auditWriter,
        IClock clock,
        IOptions<SetupOptions> setupOptions)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _auditWriter = auditWriter;
        _clock = clock;
        _configuredToken = setupOptions.Value.Token;
    }

    /// <summary>Creates the first administrator, once.</summary>
    /// <param name="command">The token and the administrator's details.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Who was created, or a typed failure.</returns>
    public async Task<Result<AuthenticatedUserDto>> HandleAsync(
        CompleteSetupCommand command,
        CancellationToken cancellationToken)
    {
        // "Any user exists" is the gate, not "the token was already spent". The token sits in a
        // file on disk for as long as the operator leaves it there; what must not happen is a
        // second administrator appearing on a panel that already has one. Checking the users
        // closes the door permanently the moment setup succeeds, whatever happens to the file.
        if (await _dbContext.Users.AnyAsync(cancellationToken))
        {
            return Result<AuthenticatedUserDto>.Fail(Error.Of(nameof(ErrorMessages.SetupAlreadyCompletedForbidden)));
        }

        if (!TokenMatches(command.Token))
        {
            return Result<AuthenticatedUserDto>.Fail(Error.Of(nameof(ErrorMessages.SetupTokenInvalidUnauthorized)));
        }

        var user = new User(
            Guid.NewGuid(),
            command.Username,
            command.Email,
            _passwordHasher.Hash(command.Password),
            UserRole.Admin,
            _clock.UtcNow);

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditWriter.WriteAsync(
            new AuditEntry(
                user.Id,
                user.Username,
                AuditActions.AdministratorCreated,
                user.Username,
                command.IpAddress,
                command.UserAgent,
                Succeeded: true),
            cancellationToken);

        return Result<AuthenticatedUserDto>.Ok(
            new AuthenticatedUserDto(user.Id, user.Username, user.Email, user.Role, user.AccountId));
    }

    /// <summary>Compares the supplied token to the configured one without leaking its length or prefix.</summary>
    /// <param name="supplied">The token the caller presented.</param>
    /// <returns>True when they match exactly.</returns>
    private bool TokenMatches(string supplied)
    {
        if (string.IsNullOrEmpty(_configuredToken))
        {
            // No token configured means nobody may claim the panel this way. An empty configured
            // value must never match an empty supplied one.
            return false;
        }

        // Fixed-time comparison: an ordinary string equality returns as soon as two bytes differ,
        // which lets a caller who can time the response discover the token one character at a time.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(_configuredToken),
            Encoding.UTF8.GetBytes(supplied));
    }
}
