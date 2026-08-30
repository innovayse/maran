using System.Security.Cryptography;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>Database-backed recovery codes, hashed with the same KDF as passwords.</summary>
/// <remarks>
/// Argon2id here, where SHA-256 was right for refresh tokens. A recovery code is short enough to be
/// written on paper and typed by a person, so it has far less entropy than a machine-generated
/// token and a leaked database of fast hashes would be worth cracking. Ten codes verified at most
/// once each, on a path a person walks rarely, can afford the cost.
/// </remarks>
public sealed class RecoveryCodeService : IRecoveryCodeService
{
    /// <summary>How many codes a user gets.</summary>
    private const int CodeCount = 10;

    /// <summary>Characters a code is drawn from: unambiguous in print and on any keyboard.</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Characters per code.</summary>
    private const int CodeLength = 10;

    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Hashes the codes.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>The panel's clock, stamping the moment a code is spent.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="passwordHasher">Hashes the codes.</param>
    /// <param name="clock">The panel's clock.</param>
    public RecoveryCodeService(IdentityDbContext dbContext, IPasswordHasher passwordHasher, IClock clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ReplaceAsync(Guid userId, CancellationToken cancellationToken)
    {
        await DiscardAsync(userId, cancellationToken);

        var codes = new List<string>(CodeCount);
        for (var index = 0; index < CodeCount; index++)
        {
            var code = GenerateCode();
            codes.Add(code);
            _dbContext.RecoveryCodes.Add(new RecoveryCode(Guid.NewGuid(), userId, _passwordHasher.Hash(code)));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return codes;
    }

    /// <inheritdoc />
    public async Task<bool> ConsumeAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.RecoveryCodes
            .Where(c => c.UserId == userId && c.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        // Every unused code is checked rather than looked up: the stored value is a salted hash, so
        // there is nothing to index by. Ten Argon2id verifications is the cost of that, and it is
        // paid on a path someone walks once, having lost their phone.
        foreach (var candidate in candidates)
        {
            if (!_passwordHasher.Verify(code, candidate.CodeHash))
            {
                continue;
            }

            candidate.Consume(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public async Task DiscardAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.RecoveryCodes.Where(c => c.UserId == userId).ToListAsync(cancellationToken);
        _dbContext.RecoveryCodes.RemoveRange(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Generates one code from the system CSPRNG.</summary>
    /// <returns>A code of <see cref="CodeLength"/> characters from <see cref="Alphabet"/>.</returns>
    private static string GenerateCode()
    {
        return RandomNumberGenerator.GetString(Alphabet, CodeLength);
    }
}
