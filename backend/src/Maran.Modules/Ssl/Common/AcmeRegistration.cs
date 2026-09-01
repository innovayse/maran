using Maran.Modules.Ssl.Services;

namespace Maran.Modules.Ssl.Common;

/// <summary>The panel's usable registration with an authority: its account URL and a signer over its key.</summary>
/// <remarks>
/// The signer is handed over rather than the key, so the PEM exists as a string in exactly one place
/// — inside the store that decrypted it — and every later user signs without ever seeing it. The
/// receiver owns the signer and disposes it.
/// </remarks>
/// <param name="AccountUrl">The account URL the authority issued, sent as <c>kid</c> on every request.</param>
/// <param name="Signer">A signer over the account key. The receiver owns and disposes it.</param>
public sealed record AcmeRegistration(string AccountUrl, AcmeSigner Signer);
