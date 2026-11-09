namespace Maran.Packaging.Models;

/// <summary>
/// The reservation for an encrypted library (<c>lib.enc</c>). Encryption here is access control for
/// distribution, not integrity and not secrecy against the licensee; this library only records and
/// checks that the entry exists, and the closed loader decrypts it.
/// </summary>
/// <param name="Alg">The cipher, a short token such as <c>aes-256-gcm</c>.</param>
/// <param name="KeyId">Which key in the licence lease opens it, a short token.</param>
public sealed record IpmEncryption(string Alg, string KeyId);
