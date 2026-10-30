using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Domain.Policies;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Ftp.Mappers;

/// <summary>Restates an <see cref="FtpUser"/> as the wire shape a screen reads.</summary>
/// <remarks>
/// The one place an <see cref="FtpUserDto"/> and its create answer are built, so the list, the single read
/// and the create answer cannot drift apart. A mapper translates; it never decides
/// (rules/csharp.md): the owner names arrive already resolved by the account directory, and this
/// only picks the row's own entry out of them.
/// </remarks>
public static class FtpUserMapper
{
    /// <summary>Builds the outward view of one FTPS login.</summary>
    /// <param name="ftpUser">The recorded login.</param>
    /// <param name="ownerNames">
    /// Account names the account directory answered for the rows being mapped, tenant-scoped. An
    /// account it did not name — gone, or not the caller's to see — is absent, and the row's owner
    /// name is then empty.
    /// </param>
    /// <returns>The wire shape carrying exactly what the row holds, and its owner's name.</returns>
    public static FtpUserDto From(FtpUser ftpUser, IReadOnlyDictionary<Guid, string> ownerNames)
    {
        ArgumentNullException.ThrowIfNull(ftpUser);
        ArgumentNullException.ThrowIfNull(ownerNames);

        return new FtpUserDto(
            ftpUser.Id,
            ftpUser.AccountId,
            ownerNames.GetValueOrDefault(ftpUser.AccountId, string.Empty),
            ftpUser.Name,
            ftpUser.FullName,
            FtpsProtocolName.Ftps,
            ftpUser.CreatedAt);
    }

    /// <summary>
    /// Builds the one-time answer to creating an FTPS login, the only view that carries its password.
    /// </summary>
    /// <param name="ftpUser">The recorded FTPS login.</param>
    /// <param name="ownerNames">
    /// Account names the account directory answered, tenant-scoped; an account it did not name is
    /// absent and the owner name is then empty, exactly as on every read.
    /// </param>
    /// <param name="password">The generated password, shown this once and kept nowhere.</param>
    /// <returns>The create answer carrying what the row holds, its owner's name and the password.</returns>
    public static CreatedFtpUserDto Created(
        FtpUser ftpUser,
        IReadOnlyDictionary<Guid, string> ownerNames,
        SensitiveString password)
    {
        ArgumentNullException.ThrowIfNull(ftpUser);
        ArgumentNullException.ThrowIfNull(ownerNames);
        ArgumentNullException.ThrowIfNull(password);

        return new CreatedFtpUserDto(
            ftpUser.Id,
            ftpUser.AccountId,
            ownerNames.GetValueOrDefault(ftpUser.AccountId, string.Empty),
            ftpUser.Name,
            ftpUser.FullName,
            FtpsProtocolName.Ftps,
            password,
            ftpUser.CreatedAt);
    }
}
