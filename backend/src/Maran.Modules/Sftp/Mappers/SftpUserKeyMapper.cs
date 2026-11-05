using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Domain.Entities;

namespace Maran.Modules.Sftp.Mappers;

/// <summary>Restates an <see cref="SftpUserKey"/> as the wire shape a screen reads.</summary>
/// <remarks>
/// The one place an <see cref="SftpUserKeyDto"/> is built, so the list and the add answer cannot
/// drift apart. It leaves the key body out on purpose (<see cref="SftpUserKeyDto"/>).
/// </remarks>
public static class SftpUserKeyMapper
{
    /// <summary>Builds the outward view of one key.</summary>
    /// <param name="key">The recorded key.</param>
    /// <returns>The wire shape carrying exactly what a screen shows about the key.</returns>
    public static SftpUserKeyDto From(SftpUserKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new SftpUserKeyDto(key.Id, key.Label, key.Algorithm, key.Fingerprint, key.CreatedAt);
    }
}
