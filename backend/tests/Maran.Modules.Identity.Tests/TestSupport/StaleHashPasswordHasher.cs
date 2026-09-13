using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// The real Argon2id hasher, except that it reports one nominated stored hash as needing an
/// upgrade. Producing a genuinely weaker hash would mean re-implementing Argon2 in the test with
/// different cost parameters; what the handler's behaviour actually depends on is the answer to
/// <see cref="IPasswordHasher.NeedsRehash"/>, so that is what this double controls.
/// </summary>
public sealed class StaleHashPasswordHasher : IPasswordHasher
{
    /// <summary>Does the real work; only the rehash verdict is overridden.</summary>
    private readonly Argon2idPasswordHasher _inner = new();

    /// <summary>The stored hash this double declares stale.</summary>
    private readonly string _staleHash;

    /// <summary>Creates the double.</summary>
    /// <param name="staleHash">The stored hash to report as needing an upgrade.</param>
    public StaleHashPasswordHasher(string staleHash)
    {
        _staleHash = staleHash;
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        return _inner.Hash(password);
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        return _inner.Verify(password, hash);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string hash)
    {
        return string.Equals(hash, _staleHash, StringComparison.Ordinal);
    }
}
