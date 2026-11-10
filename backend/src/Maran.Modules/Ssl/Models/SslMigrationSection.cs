namespace Maran.Modules.Ssl.Models;

/// <summary>
/// What the Ssl module hands to another server for one account: which certificates it holds, as metadata
/// (issue #127). No certificate, chain or key is part of a package (decision D2).
/// </summary>
/// <param name="Certificates">The account's certificates.</param>
public sealed record SslMigrationSection(IReadOnlyList<CertificateMigrationItem> Certificates);
