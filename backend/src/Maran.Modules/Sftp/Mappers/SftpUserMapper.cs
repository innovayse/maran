using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Domain.Policies;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Sftp.Mappers;

/// <summary>Restates an <see cref="SftpUser"/> as the wire shape a screen reads.</summary>
/// <remarks>
/// The one place an <see cref="SftpUserDto"/> and its create answer are built, so the list, the single read
/// and the create answer cannot drift apart. A mapper translates; it never decides
/// (rules/csharp.md): the owner names arrive already resolved by the account directory, and this
/// only picks the row's own entry out of them.
/// </remarks>
public static class SftpUserMapper
{
    /// <summary>Builds the outward view of one SFTP login.</summary>
    /// <param name="sftpUser">The recorded login.</param>
    /// <param name="ownerNames">
    /// Account names the account directory answered for the rows being mapped, tenant-scoped. An
    /// account it did not name — gone, or not the caller's to see — is absent, and the row's owner
    /// name is then empty.
    /// </param>
    /// <returns>The wire shape carrying exactly what the row holds, and its owner's name.</returns>
    public static SftpUserDto From(SftpUser sftpUser, IReadOnlyDictionary<Guid, string> ownerNames)
    {
        ArgumentNullException.ThrowIfNull(sftpUser);
        ArgumentNullException.ThrowIfNull(ownerNames);

        return new SftpUserDto(
            sftpUser.Id,
            sftpUser.AccountId,
            ownerNames.GetValueOrDefault(sftpUser.AccountId, string.Empty),
            sftpUser.Name,
            sftpUser.FullName,
            SftpProtocolName.Sftp,
            sftpUser.CreatedAt);
    }

    /// <summary>
    /// Builds the one-time answer to creating an SFTP login, the only view that carries its password.
    /// </summary>
    /// <param name="sftpUser">The recorded SFTP login.</param>
    /// <param name="ownerNames">
    /// Account names the account directory answered, tenant-scoped; an account it did not name is
    /// absent and the owner name is then empty, exactly as on every read.
    /// </param>
    /// <param name="password">The generated password, shown this once and kept nowhere.</param>
    /// <returns>The create answer carrying what the row holds, its owner's name and the password.</returns>
    public static CreatedSftpUserDto Created(
        SftpUser sftpUser,
        IReadOnlyDictionary<Guid, string> ownerNames,
        SensitiveString password)
    {
        ArgumentNullException.ThrowIfNull(sftpUser);
        ArgumentNullException.ThrowIfNull(ownerNames);
        ArgumentNullException.ThrowIfNull(password);

        return new CreatedSftpUserDto(
            sftpUser.Id,
            sftpUser.AccountId,
            ownerNames.GetValueOrDefault(sftpUser.AccountId, string.Empty),
            sftpUser.Name,
            sftpUser.FullName,
            password,
            sftpUser.CreatedAt);
    }
}
