namespace Maran.Modules.Identity.Interfaces;

/// <summary>Issues and spends the single-use codes that stand in for a lost authenticator.</summary>
public interface IRecoveryCodeService
{
    /// <summary>Replaces the user's recovery codes with a fresh set.</summary>
    /// <param name="userId">Whose codes to replace.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The new codes in plaintext — the only time they exist in readable form, since only their
    /// hashes are stored.
    /// </returns>
    Task<IReadOnlyList<string>> ReplaceAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Spends one of the user's codes, if it matches an unused one.</summary>
    /// <param name="userId">Whose codes to check.</param>
    /// <param name="code">The code the user typed.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>True when a code matched and has now been spent.</returns>
    Task<bool> ConsumeAsync(Guid userId, string code, CancellationToken cancellationToken);

    /// <summary>Discards every code a user has.</summary>
    /// <param name="userId">Whose codes to discard.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once the codes are gone.</returns>
    Task DiscardAsync(Guid userId, CancellationToken cancellationToken);
}
