namespace Maran.SharedKernel.Interfaces;

/// <summary>
/// Derives a key for one named purpose from the panel's secret, so a feature that needs a key of its own
/// does not use the secret itself and does not need a new one stored beside it.
/// </summary>
/// <remarks>
/// Two purposes give two unrelated keys, and learning one reveals neither the other nor the secret. The
/// purpose is part of what is stored against the result: changing its spelling changes every key derived
/// under it, so a purpose label carries a version (<c>maran/audit-chain/v1</c>). Rotating the panel's
/// secret changes every derived key as well.
/// </remarks>
public interface ISubKeyDeriver
{
    /// <summary>Derives the key for <paramref name="purpose"/>.</summary>
    /// <param name="purpose">A non-empty, versioned label naming what the key is for.</param>
    /// <returns>A new 32-byte key; the same purpose always yields the same bytes for one panel secret.</returns>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> is null, empty or blank.</exception>
    byte[] DeriveKey(string purpose);
}
