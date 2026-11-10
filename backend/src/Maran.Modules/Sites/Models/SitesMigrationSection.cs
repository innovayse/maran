namespace Maran.Modules.Sites.Models;

/// <summary>
/// What the Sites module hands to another server for one account: each site's hostnames and backend, and
/// nothing that was written under the home directory (issue #127).
/// </summary>
/// <remarks>
/// A site's files travel in the account's backup, not here, and a certificate never travels: ACME certificates
/// are issued again on the new server once DNS points at it, and uploaded ones are reported as needing to be
/// uploaded again (decision D2). No private key is a field of this record.
/// </remarks>
/// <param name="Sites">The account's sites.</param>
public sealed record SitesMigrationSection(IReadOnlyList<SiteMigrationItem> Sites);
