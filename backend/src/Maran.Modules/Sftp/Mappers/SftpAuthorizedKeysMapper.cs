using Maran.Modules.Sftp.Domain.Entities;

namespace Maran.Modules.Sftp.Mappers;

/// <summary>Restates a login's key rows as the full key set the agent is sent.</summary>
/// <remarks>
/// The agent converges to exactly the list it receives, so every call carries the COMPLETE set read
/// from the panel's rows. The order is fixed (registration instant, then identity) so the same rows
/// always produce the same list, which keeps the agent's replace idempotent and its file stable.
/// </remarks>
public static class SftpAuthorizedKeysMapper
{
    /// <summary>Builds the one-line key list for a login.</summary>
    /// <param name="keys">The login's key rows, all of them.</param>
    /// <returns>Each key as <c>&lt;algorithm&gt; &lt;base64&gt;</c>, oldest first.</returns>
    public static IReadOnlyList<string> From(IEnumerable<SftpUserKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        return keys
            .OrderBy(key =>
            {
                return key.CreatedAt;
            })
            .ThenBy(key =>
            {
                return key.Id;
            })
            .Select(key =>
            {
                return key.PublicKey;
            })
            .ToList();
    }
}
