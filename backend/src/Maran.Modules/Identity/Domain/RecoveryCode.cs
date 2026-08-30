namespace Maran.Modules.Identity.Domain;

/// <summary>
/// One single-use code that stands in for a TOTP code when the user has lost their authenticator
/// (spec §10). Stored as a hash: a recovery code is a password the user did not choose, and a
/// database leak must not hand over the second factor of every account.
/// </summary>
public sealed class RecoveryCode
{
    /// <summary>The code's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The user this code belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The Argon2id hash of the code.</summary>
    public string CodeHash { get; private set; }

    /// <summary>The instant the code was spent; null while it is still usable.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>Creates an unused recovery code.</summary>
    /// <param name="id">The code's identity.</param>
    /// <param name="userId">The user this code belongs to.</param>
    /// <param name="codeHash">The Argon2id hash of the code as shown to the user.</param>
    public RecoveryCode(Guid id, Guid userId, string codeHash)
    {
        Id = id;
        UserId = userId;
        CodeHash = codeHash;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private RecoveryCode()
    {
        CodeHash = string.Empty;
    }

    /// <summary>Reports whether the code may still be used.</summary>
    /// <returns>True when the code has not been spent.</returns>
    public bool IsUsable()
    {
        return ConsumedAt is null;
    }

    /// <summary>Spends the code. A no-op once spent, so a replay cannot refresh its timestamp.</summary>
    /// <param name="at">The instant the code was used, taken from <see cref="IClock"/>.</param>
    public void Consume(DateTimeOffset at)
    {
        ConsumedAt ??= at;
    }
}
