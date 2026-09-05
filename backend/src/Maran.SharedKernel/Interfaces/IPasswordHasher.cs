namespace Maran.SharedKernel.Interfaces;

/// <summary>
/// Hashes and verifies panel passwords. The only way a password is ever turned into something
/// storable — no caller hashes on its own (rules/security.md item 9).
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password with a fresh random salt.</summary>
    /// <param name="password">The plaintext password. Never logged, never stored, never echoed back.</param>
    /// <returns>The encoded hash, in PHC string format, safe to store.</returns>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash, comparing in constant time.</summary>
    /// <param name="password">The plaintext password supplied by the caller.</param>
    /// <param name="hash">The stored encoded hash.</param>
    /// <returns>
    /// True when the password matches. False when it does not, and also when the stored hash is
    /// malformed or names a different algorithm: a corrupt row must fail one login, not the process.
    /// </returns>
    bool Verify(string password, string hash);

    /// <summary>Reports whether a stored hash was produced with weaker parameters than the current ones.</summary>
    /// <param name="hash">The stored encoded hash.</param>
    /// <returns>True when the hash should be recomputed on its owner's next successful login.</returns>
    bool NeedsRehash(string hash);
}
