namespace Maran.Modules.Backups.Domain.Enums;

/// <summary>How far one backup got, and therefore whether its artifact may be relied on.</summary>
/// <remarks>
/// Three values and no fourth, deliberately. There is no <c>Partial</c>: an archive that is missing
/// a database is an archive a restore must not be offered, so anything short of a complete artifact
/// is <see cref="Failed"/>. The plan's R7 states the same rule for a restore and for the same
/// reason — "nothing threw" is not a completion.
/// </remarks>
public enum BackupStatus
{
    /// <summary>The agent is still working. The artifact does not exist yet and may never.</summary>
    Running = 0,

    /// <summary>The artifact exists, its digest is recorded, and a restore may read it.</summary>
    Completed = 1,

    /// <summary>The run ended without a usable artifact. <c>FailureCode</c> says why.</summary>
    Failed = 2,
}
