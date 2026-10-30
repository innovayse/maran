using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Domain.Enums;

namespace Maran.Modules.Ftp.Domain.Policies;

/// <summary>
/// The one place a settings row — or its absence — becomes the panel's <see cref="FtpsDesiredState"/>.
/// </summary>
/// <remarks>
/// The absent row is the case this exists for. It used to become <c>Enabled: false</c> inside the
/// mapper, which made a server nobody had configured indistinguishable from one an administrator had
/// switched off. Stating the translation once, here, means every handler that reads the row answers
/// the same three-valued question the same way.
/// </remarks>
public static class FtpsDesiredStatePolicy
{
    /// <summary>States what the panel wants, from the row it persisted.</summary>
    /// <param name="settings">The settings row, or <c>null</c> when FTPS has never been turned on here.</param>
    /// <returns>
    /// <see cref="FtpsDesiredState.NeverConfigured"/> for no row; otherwise <see cref="FtpsDesiredState.On"/>
    /// or <see cref="FtpsDesiredState.Off"/> as the row records.
    /// </returns>
    public static FtpsDesiredState Of(FtpsSettings? settings)
    {
        if (settings is null)
        {
            return FtpsDesiredState.NeverConfigured;
        }

        return settings.Enabled ? FtpsDesiredState.On : FtpsDesiredState.Off;
    }
}
