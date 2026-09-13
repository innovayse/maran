using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Commands.CompleteSetup;

/// <summary>Handles <see cref="CompleteSetupCommand"/> by creating the panel's first administrator.</summary>
/// <remarks>
/// <para>
/// <b>Three gates, in this order, and each answers a different question.</b> Whether the panel
/// already HAS an owner (state, not a spent-flag); whether the token is the configured one (a
/// constant-time comparison); and whether that token is still inside its window. The order matters
/// for what a caller learns: a panel that is already claimed says so without the token being
/// examined at all, so a stranger probing a live server cannot distinguish a wrong token from a
/// right one there.
/// </para>
/// <para>
/// <b>The window is the answer to an install nobody finished.</b> Until it existed, this handler
/// gated on "does any user exist" alone, so a server installed and abandoned kept a live token — 192
/// bits of permission to own it — for as long as the file holding it existed. The window's length,
/// where its clock comes from, and what an operator does when it has closed are all documented on
/// <see cref="SetupTokenWindowKeeper"/>; the refusal names the remedy, because a dead end here is a
/// server nobody can claim.
/// </para>
/// </remarks>
public sealed class CompleteSetupCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Hashes the chosen password.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>Records the creation.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The authority on whether the configured token is still inside its window.</summary>
    private readonly SetupTokenWindowKeeper _windowKeeper;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The configured one-time token.</summary>
    private readonly string _configuredToken;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Hashes the chosen password.</param>
    /// <param name="journal">Records the creation.</param>
    /// <param name="windowKeeper">Decides whether the configured token is still inside its window.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="setupOptions">The bound <see cref="SetupOptions"/>, carrying the installer's token.</param>
    public CompleteSetupCommandHandler(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IdentityAuditJournal journal,
        SetupTokenWindowKeeper windowKeeper,
        IClock clock,
        IOptions<SetupOptions> setupOptions)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _journal = journal;
        _windowKeeper = windowKeeper;
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
            return Result<AuthenticatedUserDto>.Fail(Error.Of(nameof(ErrorMessages.SetupAlreadyCompletedForbidden), ErrorType.Forbidden));
        }

        if (!TokenMatches(command.Token))
        {
            return Result<AuthenticatedUserDto>.Fail(Error.Of(nameof(ErrorMessages.SetupTokenInvalidUnauthorized), ErrorType.Unauthorized));
        }

        // Last, and only once the token is known to be the right one: an expiry reported to a caller
        // who guessed wrong would tell them the token they guessed was otherwise acceptable.
        if (await _windowKeeper.HasExpiredAsync(command.Token, cancellationToken))
        {
            return Result<AuthenticatedUserDto>.Fail(Error.Of(nameof(ErrorMessages.SetupTokenExpiredUnauthorized), ErrorType.Unauthorized));
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

        await _journal.RecordClaimAsync(
            user.Id,
            user.Username,
            AuditActions.AdministratorCreated,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
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
