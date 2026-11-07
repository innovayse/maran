using System.Globalization;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// Spells the audit subjects of the retention entries, so a change of the window and every run of it
/// name the window the same way, and "keep indefinitely" is one fixed word and never an empty string.
/// </summary>
/// <remarks>
/// A retention run is journalled whether or not it deleted anything (threat note, section 2), and the
/// subject is how a reader of the journal learns how much was destroyed and through which chain index.
/// Nothing here reads a row: every value is a number or an instant, so no personal data can reach a
/// subject through this type.
/// </remarks>
public static class AuditRetentionSubjectText
{
    /// <summary>The word written where the window is "no limit".</summary>
    public const string KeepIndefinitely = "indefinite";

    /// <summary>The word written where a run had no cutoff because retention was off.</summary>
    public const string NoCutoff = "none";

    /// <summary>Builds <c>from &lt;old&gt; to &lt;new&gt;</c> for a change of the window.</summary>
    /// <param name="previousDays">The window before the change; <c>null</c> for "indefinite".</param>
    /// <param name="newDays">The window after the change; <c>null</c> for "indefinite".</param>
    /// <returns>The subject text recorded with the change entry.</returns>
    public static string Change(int? previousDays, int? newDays)
    {
        return $"from {Days(previousDays)} to {Days(newDays)}";
    }

    /// <summary>Builds <c>retentionDays=… cutoff=… purged=N throughChainIndex=M</c> for a finished run.</summary>
    /// <param name="retentionDays">The window the run applied; <c>null</c> for "indefinite".</param>
    /// <param name="cutoff">The instant older rows were eligible after, or <c>null</c> when there was none.</param>
    /// <param name="purged">How many rows the run deleted.</param>
    /// <param name="throughChainIndex">The highest purged chain index after the run.</param>
    /// <returns>The subject text recorded with the run entry.</returns>
    public static string Run(int? retentionDays, DateTimeOffset? cutoff, int purged, long throughChainIndex)
    {
        var cutoffText = cutoff is null
            ? NoCutoff
            : cutoff.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"retentionDays={Days(retentionDays)} cutoff={cutoffText} purged={purged} throughChainIndex={throughChainIndex}");
    }

    /// <summary>Builds <c>retentionDays=… failed</c> for a run that could not finish.</summary>
    /// <param name="retentionDays">The window the run was to apply; <c>null</c> for "indefinite".</param>
    /// <returns>The subject text recorded with the failed run entry.</returns>
    public static string Failed(int? retentionDays)
    {
        return $"retentionDays={Days(retentionDays)} failed";
    }

    /// <summary>Spells one window.</summary>
    /// <param name="days">The window in days, or <c>null</c> for "indefinite".</param>
    /// <returns>The number of days in invariant digits, or <see cref="KeepIndefinitely"/>.</returns>
    private static string Days(int? days)
    {
        return days is null ? KeepIndefinitely : days.Value.ToString(CultureInfo.InvariantCulture);
    }
}
