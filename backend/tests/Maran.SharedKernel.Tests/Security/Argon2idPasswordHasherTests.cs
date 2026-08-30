using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>Behavioral contract of <see cref="Argon2idPasswordHasher"/>.</summary>
public sealed class Argon2idPasswordHasherTests
{
    /// <summary>A password long enough to be realistic; reused so the assertions read as one story.</summary>
    private const string Password = "correct horse battery staple";

    private readonly Argon2idPasswordHasher _hasher = new();

    /// <summary>Hashing the same password twice produces different hashes.</summary>
    [Fact]
    public void Hashing_the_same_password_twice_produces_different_hashes()
    {
        var first = _hasher.Hash(Password);
        var second = _hasher.Hash(Password);

        Assert.NotEqual(first, second);
    }

    /// <summary>Verifying the original password succeeds.</summary>
    [Fact]
    public void Verifying_the_original_password_succeeds()
    {
        var hash = _hasher.Hash(Password);

        Assert.True(_hasher.Verify(Password, hash));
    }

    /// <summary>Verifying a different password fails.</summary>
    [Fact]
    public void Verifying_a_different_password_fails()
    {
        var hash = _hasher.Hash(Password);

        Assert.False(_hasher.Verify("correct horse battery stapl", hash));
    }

    /// <summary>Verifying against a malformed hash returns false instead of throwing.</summary>
    [Fact]
    public void Verifying_against_a_malformed_hash_returns_false_instead_of_throwing()
    {
        Assert.False(_hasher.Verify(Password, "not-a-hash"));
    }

    /// <summary>Verifying against a hash naming another algorithm returns false.</summary>
    [Fact]
    public void Verifying_against_a_hash_naming_another_algorithm_returns_false()
    {
        var hash = _hasher.Hash(Password).Replace("$argon2id$", "$argon2i$", StringComparison.Ordinal);

        Assert.False(_hasher.Verify(Password, hash));
    }

    /// <summary>A hash produced with weaker parameters needs rehashing.</summary>
    [Fact]
    public void A_hash_produced_with_weaker_parameters_needs_rehashing()
    {
        // Half the current memory cost: exactly the migration case NeedsRehash exists for —
        // the parameters were raised after this hash was stored.
        var weaker = _hasher.Hash(Password)
            .Replace($"m={PasswordHashParameters.MemoryKib}", $"m={PasswordHashParameters.MemoryKib / 2}", StringComparison.Ordinal);

        Assert.True(_hasher.NeedsRehash(weaker));
    }

    /// <summary>A hash produced with the current parameters does not need rehashing.</summary>
    [Fact]
    public void A_hash_produced_with_the_current_parameters_does_not_need_rehashing()
    {
        Assert.False(_hasher.NeedsRehash(_hasher.Hash(Password)));
    }

    /// <summary>A malformed hash needs rehashing.</summary>
    [Fact]
    public void A_malformed_hash_needs_rehashing()
    {
        Assert.True(_hasher.NeedsRehash("not-a-hash"));
    }
}
