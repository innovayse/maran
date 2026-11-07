namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// How long the audit journal is kept: at most one row, ever. A null period means the journal is
/// kept indefinitely, which is what a fresh panel does.
/// </summary>
/// <remarks>
/// <para>
/// <b>A singleton by construction,</b> as <see cref="SecurityPolicy"/> is: the primary key is
/// <see cref="SingletonId"/> and nothing generates another, so a second row cannot exist and "which
/// policy applies" has one answer.
/// </para>
/// <para>
/// <b>The bounds are the entity's rule, not only the validator's.</b> A command dispatched on the bus
/// skips the HTTP validator, and a period of zero or one day would let a single call destroy the
/// journal at the next run. The constructor and <see cref="Replace"/> therefore refuse a period
/// outside <see cref="MinimumDays"/> to <see cref="MaximumDays"/>; "delete everything now" cannot be
/// expressed.
/// </para>
/// </remarks>
public sealed class AuditRetentionPolicy
{
    /// <summary>The one primary key this table ever holds.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000004152");

    /// <summary>
    /// The period a fresh panel keeps the journal for: null, meaning indefinitely. The journal has been
    /// documented as never deleted, and an upgrade that starts destroying security records on its own
    /// would be a surprise nobody asked for; an operator who needs a limit opts in. Declared without an
    /// initializer because null IS the value, and spelling it out would be flagged as a redundant default.
    /// </summary>
    public static readonly int? DefaultRetentionDays;

    /// <summary>The shortest period that may be set, in days.</summary>
    public const int MinimumDays = 30;

    /// <summary>The longest period that may be set, in days.</summary>
    public const int MaximumDays = 3650;

    /// <summary>The row's identity; always <see cref="SingletonId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>How many days an entry is kept; null keeps the journal indefinitely.</summary>
    public int? RetentionDays { get; private set; }

    /// <summary>When the policy was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the daily maintenance last ran retention; null if it has not yet.</summary>
    public DateTimeOffset? LastRunAt { get; private set; }

    /// <summary>Creates the retention policy for the first time.</summary>
    /// <param name="retentionDays">How many days an entry is kept, or null to keep the journal indefinitely.</param>
    /// <param name="updatedAt">When the policy was saved, from the panel's clock.</param>
    /// <exception cref="ArgumentOutOfRangeException">The period is outside <see cref="MinimumDays"/> to <see cref="MaximumDays"/>.</exception>
    public AuditRetentionPolicy(int? retentionDays, DateTimeOffset updatedAt)
    {
        RequirePermitted(retentionDays);

        Id = SingletonId;
        RetentionDays = retentionDays;
        UpdatedAt = updatedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AuditRetentionPolicy()
    {
    }

    /// <summary>Whether a period is one the policy accepts: null, or inside the bounds.</summary>
    /// <param name="retentionDays">The period to check.</param>
    /// <returns>True when <see cref="Replace"/> would accept it.</returns>
    public static bool IsPermitted(int? retentionDays)
    {
        return retentionDays is null || (retentionDays >= MinimumDays && retentionDays <= MaximumDays);
    }

    /// <summary>Replaces the retention period with the one just saved.</summary>
    /// <param name="retentionDays">How many days an entry is kept, or null to keep the journal indefinitely.</param>
    /// <param name="updatedAt">When the policy was saved, from the panel's clock.</param>
    /// <exception cref="ArgumentOutOfRangeException">The period is outside <see cref="MinimumDays"/> to <see cref="MaximumDays"/>.</exception>
    public void Replace(int? retentionDays, DateTimeOffset updatedAt)
    {
        RequirePermitted(retentionDays);

        RetentionDays = retentionDays;
        UpdatedAt = updatedAt;
    }

    /// <summary>Records that the daily maintenance ran retention, so the next run is a day later.</summary>
    /// <param name="at">When the run happened, from the panel's clock.</param>
    public void RecordRun(DateTimeOffset at)
    {
        LastRunAt = at;
    }

    /// <summary>Refuses a period that is neither null nor inside the bounds.</summary>
    /// <param name="retentionDays">The period to check.</param>
    private static void RequirePermitted(int? retentionDays)
    {
        if (!IsPermitted(retentionDays))
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays),
                retentionDays,
                $"Retention must be null or between {MinimumDays} and {MaximumDays} days.");
        }
    }
}
