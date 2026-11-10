namespace Maran.Sdk.Contracts;

/// <summary>
/// A signed, short-lived statement that a licence is still good, as a lease source hands it to the
/// panel AFTER checking its signature. It names the licence it renews and the span it covers and holds
/// nothing else: no decryption key (those belong to the module loader's own window), no signature and
/// no licence text.
/// </summary>
/// <param name="LicenceId">The id of the licence this lease renews.</param>
/// <param name="IssuedAt">When the lease was issued, by the issuer's clock.</param>
/// <param name="ValidUntil">When the lease stops being valid. A lease is issued for 14 days and renewed daily.</param>
/// <param name="MachineId">The machine id the lease is bound to, or null for a lease bound to no server.</param>
public sealed record LicenceLease(string LicenceId, DateTimeOffset IssuedAt, DateTimeOffset ValidUntil, string? MachineId);
