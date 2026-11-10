using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Domain.Policies;

/// <summary>
/// Decides what the start-up reconciliation does with a ledger row, given whether the host has the app.
/// A pure rule over two facts, so every case is a test and the hosted service only applies the answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The host wins.</b> The ledger is the panel's memory of what it asked for; the host is what is
/// true. A process that died between "installing" and the end of the install, an app removed from a
/// shell, one installed from a shell: all of them leave the ledger saying something the host does not,
/// and the host is the side that is right.
/// </para>
/// <para>
/// <b>An install that left nothing behind is Failed, not deleted.</b> The administrator asked for it and
/// the answer to "what happened" is a row that says it failed, beside the task that says why. A
/// removal that left nothing behind is finished, and an installed row the host no longer has is an app
/// somebody removed by hand: both are simply gone from the ledger.
/// </para>
/// </remarks>
public static class AppLedgerPolicy
{
    /// <summary>Judges one row.</summary>
    /// <param name="row">The ledger row.</param>
    /// <param name="hostVersion">
    /// The version the host reports for the app, or <see langword="null"/> when the host does not have it.
    /// An app the host reports with no version is the empty string, which is a version that is not known
    /// and not an app that is absent.
    /// </param>
    /// <returns>What to do with the row.</returns>
    public static AppLedgerVerdict Decide(InstalledApp row, string? hostVersion)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (hostVersion is not null)
        {
            // A package updated from a shell changes the version and nothing else, so an installed row
            // whose version differs is refreshed rather than kept.
            return row.State == AppState.Installed && string.Equals(row.Version, hostVersion, StringComparison.Ordinal)
                ? AppLedgerVerdict.Keep
                : AppLedgerVerdict.MarkInstalled;
        }

        return row.State switch
        {
            AppState.Installing => AppLedgerVerdict.MarkFailed,
            AppState.Failed => AppLedgerVerdict.Keep,
            _ => AppLedgerVerdict.Delete,
        };
    }
}
