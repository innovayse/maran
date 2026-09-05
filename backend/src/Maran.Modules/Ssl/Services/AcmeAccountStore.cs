using System.Text.Json;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Resources;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Holds the panel's ACME registration: creates it against an authority the first time a certificate
/// is ordered, and reuses it forever after.
/// </summary>
/// <remarks>
/// Reuse is the point. Creating an account is itself rate-limited, and an account carries the
/// authorizations this server has already earned — so a client that registers afresh on every order
/// burns a limit AND throws away the validations it just paid for. One row per directory URL, because
/// staging and production are different authorities and a key registered with one is meaningless to
/// the other.
/// </remarks>
public sealed class AcmeAccountStore
{
    /// <summary>The Ssl module's database context, which owns the <c>ssl</c> schema.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Handed to the session this store opens, so a rejected registration is diagnosable.</summary>
    private readonly ILogger<AcmeAccountStore> _logger;

    /// <summary>Creates the store.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="clock">The injected time source used to stamp a new registration.</param>
    /// <param name="logger">Sink for the authority's machine-readable refusals.</param>
    public AcmeAccountStore(SslDbContext dbContext, IClock clock, ILogger<AcmeAccountStore> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Returns the registration for an authority, creating it on first use.</summary>
    /// <param name="http">The named, already-governed ACME client.</param>
    /// <param name="directory">The authority's directory document, which names <c>newAccount</c> and <c>newNonce</c>.</param>
    /// <param name="directoryUrl">The authority's directory URL, which identifies the row.</param>
    /// <param name="contactEmail">The operator's contact address to register.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The registration, or a typed failure. The caller owns and disposes the signer.</returns>
    public async Task<Result<AcmeRegistration>> GetOrCreateAsync(
        HttpClient http,
        JsonElement? directory,
        string directoryUrl,
        string contactEmail,
        CancellationToken cancellationToken)
    {
        // No tenant filter applies: an AcmeAccount has no AccountId, because the panel — not a
        // customer — is the authority's customer.
        var existing = await _dbContext.AcmeAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(account => account.DirectoryUrl == directoryUrl, cancellationToken);
        if (existing is not null)
        {
            return Result<AcmeRegistration>.Ok(
                new AcmeRegistration(existing.AccountUrl, AcmeSigner.FromPem(existing.PrivateKeyPem)));
        }

        return await RegisterAsync(http, directory, directoryUrl, contactEmail, cancellationToken);
    }

    /// <summary>Registers a brand-new account with the authority and stores its key.</summary>
    /// <param name="http">The named, already-governed ACME client.</param>
    /// <param name="directory">The authority's directory document.</param>
    /// <param name="directoryUrl">The authority's directory URL, which identifies the row.</param>
    /// <param name="contactEmail">The operator's contact address to register.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The new registration, or a typed failure.</returns>
    private async Task<Result<AcmeRegistration>> RegisterAsync(
        HttpClient http,
        JsonElement? directory,
        string directoryUrl,
        string contactEmail,
        CancellationToken cancellationToken)
    {
        if (directory is not { } document)
        {
            return Result<AcmeRegistration>.Fail(Error.Of(nameof(ErrorMessages.AcmeAuthorityUnreachable), ErrorType.Unavailable));
        }

        var signer = AcmeSigner.CreateNew();
        var succeeded = false;
        try
        {
            // No kid yet: an account that does not exist has no URL, so this one request identifies
            // itself by the key it is signed with (RFC 8555 §7.3).
            var session = new AcmeSession(http, signer, accountUrl: null, _logger);
            await session.RefreshNonceAsync(Member(document, "newNonce"), cancellationToken);

            var payload = JsonSerializer.Serialize(new
            {
                termsOfServiceAgreed = true,
                contact = new[] { $"mailto:{contactEmail}" },
            });

            var created = await session.PostAsync(Member(document, "newAccount"), payload, cancellationToken);
            if (!created.IsSuccess || created.Value.Location.Length == 0)
            {
                return Result<AcmeRegistration>.Fail(Error.Of(nameof(ErrorMessages.AcmeAccountRejected), ErrorType.Failure));
            }

            _dbContext.AcmeAccounts.Add(new AcmeAccount(
                Guid.NewGuid(),
                directoryUrl,
                created.Value.Location,
                signer.ExportPrivateKeyPem(),
                _clock.UtcNow));
            await _dbContext.SaveChangesAsync(cancellationToken);

            succeeded = true;
            return Result<AcmeRegistration>.Ok(new AcmeRegistration(created.Value.Location, signer));
        }
        finally
        {
            // The signer is handed to the caller on success and is theirs to dispose. On every
            // failure path it is disposed here instead, so a rejected registration does not leak a
            // key into the finalizer queue.
            if (!succeeded)
            {
                signer.Dispose();
            }
        }
    }

    /// <summary>Reads one string member of the directory document.</summary>
    /// <param name="document">The directory document.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member's text, or the empty string when absent.</returns>
    private static string Member(JsonElement document, string name)
    {
        return document.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
