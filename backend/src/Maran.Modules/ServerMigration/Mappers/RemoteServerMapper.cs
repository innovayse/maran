using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// Restates a stored remote server as its wire shapes. It copies fields and builds the one derived line the
/// administrator installs; it carries no private key material by construction — the entity's private key is not read
/// anywhere in this file.
/// </summary>
public static class RemoteServerMapper
{
    /// <summary>Restates a server for the list.</summary>
    /// <param name="server">The stored server.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <returns>The list shape.</returns>
    public static RemoteServerDto ToDto(RemoteServer server, IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(names);

        return new RemoteServerDto(
            server.Id,
            server.Name,
            server.Host,
            server.Port,
            server.SshUser,
            server.State,
            MigrationEnumNames.Of(names, server.State),
            server.HostKeyFingerprint,
            server.LastTestedAt,
            server.Kind,
            MigrationEnumNames.Of(names, server.Kind),
            server.AccountsFound,
            server.CreatedAt);
    }

    /// <summary>Restates a server in full.</summary>
    /// <param name="server">The stored server, with its host key candidates loaded.</param>
    /// <param name="warnings">What the last discovery could not read, already worded for the caller.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <returns>The detail shape.</returns>
    public static RemoteServerDetailDto ToDetail(
        RemoteServer server,
        IReadOnlyList<DiscoveryWarningDto> warnings,
        IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(names);

        var candidates = server.HostKeyCandidates
            .OrderBy(
                candidate =>
                {
                    return candidate.Algorithm;
                },
                StringComparer.Ordinal)
            .Select(candidate =>
            {
                return new RemoteServerHostKeyCandidateDto(candidate.Algorithm, candidate.Fingerprint);
            })
            .ToList();

        return new RemoteServerDetailDto(
            server.Id,
            server.Name,
            server.Host,
            server.Port,
            server.SshUser,
            server.State,
            MigrationEnumNames.Of(names, server.State),
            server.HostKeyFingerprint,
            server.LastTestedAt,
            server.Kind,
            MigrationEnumNames.Of(names, server.Kind),
            server.AccountsFound,
            server.CreatedAt,
            server.AllowedFrom,
            server.PublicKey,
            AuthorizedKeysLineMapper.From(server.PublicKey, server.AllowedFrom),
            server.HostKeyAlgorithm,
            server.HostKeyConfirmedAt,
            candidates,
            server.LastFailure,
            MigrationEnumNames.Of(names, server.LastFailure),
            server.SystemRelease,
            server.MaranVersion,
            server.IsRoot,
            server.HomeFreeBytes,
            server.HomeTotalBytes,
            server.DatabaseServer,
            server.WebServer,
            server.MailFound,
            server.DiscoveredAt,
            warnings);
    }
}
