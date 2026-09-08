using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Resolves the operator-facing name of a backup destination in the current request's culture,
/// for the one destination whose name the panel wrote itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>The stored name stays English and that is correct; the DISPLAY is still the backend's.</b>
/// <c>DefaultBackupDestinationSeeder</c> writes <c>Local storage</c> and argues, rightly, that a
/// name translated at write time would be frozen in whichever language the server first booted in,
/// and would mean two different things to a screen and to an operator reading the table in
/// <c>psql</c>. What did not follow from that is showing the stored bytes as a heading: the
/// destinations screen rendered <c>Local storage</c> in the middle of a Russian page. The row keeps
/// its English name, and this states what to show for it (rules/architecture.md "The backend owns
/// the data, the SPA renders it").
/// </para>
/// <para>
/// <b>Only the seeded row is named here, and the test is its fixed identity rather than its text.</b>
/// A destination an operator saved carries a name the OPERATOR chose, and translating that would be
/// putting words in their mouth — it is shown verbatim, in whatever language they typed it. Keying
/// on <see cref="BackupDestination.DefaultDestinationId"/> rather than on the stored name is what
/// keeps that line exact: an operator who one day names their own destination "Local storage" gets
/// their own words back, not the panel's translation of them.
/// </para>
/// </remarks>
public sealed class BackupDestinationDisplayNames
{
    /// <summary>The resx key naming the destination this panel seeds for itself.</summary>
    private const string DefaultDestinationKey = "BackupDestinationDefault";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public BackupDestinationDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one destination as an operator reads it.</summary>
    /// <param name="destinationId">The destination's identity.</param>
    /// <param name="storedName">The name recorded on the row.</param>
    /// <returns>
    /// The localized name for the panel's own seeded destination; <paramref name="storedName"/>
    /// unchanged for every destination an operator named, and for the seeded one on a build that
    /// carries no entry for it.
    /// </returns>
    public string Of(Guid destinationId, string storedName)
    {
        if (destinationId != BackupDestination.DefaultDestinationId)
        {
            return storedName;
        }

        var localized = _displayNames[DefaultDestinationKey];

        return localized.ResourceNotFound ? storedName : localized.Value;
    }
}
