namespace Maran.Sdk.Interfaces;

/// <summary>
/// The panel's current licence lease as the module loader needs it: which modules it entitles, the key
/// that opens a protected package, and when it stops being valid. A read-only window; the licensing
/// module owns the lease and answers it.
/// </summary>
public interface ILicenceLease
{
    /// <summary>When the lease stops being valid.</summary>
    DateTimeOffset ExpiresAt { get; }

    /// <summary>Whether the lease covers a module.</summary>
    /// <param name="moduleId">The module's <see cref="Contracts.Manifest.Id"/>.</param>
    /// <returns>True when the module may run under this lease.</returns>
    bool IsEntitled(string moduleId);

    /// <summary>The key that decrypts a module's package.</summary>
    /// <param name="moduleId">The module's <see cref="Contracts.Manifest.Id"/>.</param>
    /// <returns>The key bytes, or null when the lease holds none for that module.</returns>
    byte[]? DecryptionKeyFor(string moduleId);
}
