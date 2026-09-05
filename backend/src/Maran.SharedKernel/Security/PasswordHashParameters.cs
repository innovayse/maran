namespace Maran.SharedKernel.Security;

/// <summary>
/// The Argon2id cost parameters every panel password hash is produced with, in one place so that
/// raising them is a single edit and <see cref="Interfaces.IPasswordHasher.NeedsRehash"/> can
/// compare a stored hash against them.
/// </summary>
/// <remarks>
/// 64 MiB with three passes and two lanes is the OWASP Password Storage Cheat Sheet's second
/// configuration, chosen deliberately over the 19 MiB first one: the panel authenticates a handful
/// of operators, not a consumer login wall, so a hash costing on the order of a tenth of a second
/// is affordable per login and expensive per billion guesses. The values are constants rather than
/// configuration because an operator who lowered them would silently weaken every password set
/// afterwards, and the only safe direction — raising them — is already served by
/// <see cref="Argon2idPasswordHasher.NeedsRehash"/> upgrading each hash on its owner's next login.
/// </remarks>
public static class PasswordHashParameters
{
    /// <summary>Memory cost, in kibibytes.</summary>
    public const int MemoryKib = 65536;

    /// <summary>Number of passes over memory.</summary>
    public const int Iterations = 3;

    /// <summary>Degree of parallelism, in lanes.</summary>
    public const int Parallelism = 2;

    /// <summary>Length of the random per-password salt, in bytes.</summary>
    public const int SaltBytes = 16;

    /// <summary>Length of the derived hash, in bytes.</summary>
    public const int HashBytes = 32;
}
