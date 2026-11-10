using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// One whole-server migration: a set of a registered server's accounts that are created here and synced, in
/// parallel up to a limit, and cut over when the administrator says so (issue #197, phase 2).
/// </summary>
/// <remarks>
/// <para>
/// The job holds the settings that apply to all of its accounts (concurrency, bandwidth) and its own state; each
/// account's mapping and progress is a <see cref="MigrationJobAccount"/>. Every change of state is a method here,
/// and the methods return a failure for a transition the job does not allow instead of assigning anyway, so a
/// handler cannot move a completed job back to running.
/// </para>
/// <para>
/// Administrator bookkeeping and not a tenant row: it holds the id of the administrator who started it
/// (<see cref="CreatedByUserId"/>), which is who the engine acts as for every step, re-checked against the user table
/// each time; and it carries no account id of the panel's own for a filter to close over.
/// </para>
/// </remarks>
public sealed class MigrationJob
{
    /// <summary>The longest label.</summary>
    public const int NameMaxLength = 80;

    /// <summary>The longest origin of a source panel.</summary>
    public const int SourceUrlMaxLength = 300;

    /// <summary>The length of a key fingerprint (lowercase hexadecimal SHA-256).</summary>
    public const int FingerprintLength = 64;

    /// <summary>The job's identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The registered server the accounts come from.</summary>
    public Guid RemoteServerId { get; private set; }

    /// <summary>The administrator's label for the job.</summary>
    public string Name { get; private set; }

    /// <summary>Where the job is.</summary>
    public MigrationJobState State { get; private set; }

    /// <summary>How many accounts are transferred at once, 1 to 8.</summary>
    public int Concurrency { get; private set; }

    /// <summary>The most KiB per second a transfer reads, or <c>null</c> for no limit. Applies from the next slice of every transfer.</summary>
    public int? BandwidthLimitKib { get; private set; }

    /// <summary>
    /// For a source that is a Maran panel: the https origin of its pull endpoint, where each account's package is
    /// fetched with the grant its row holds. <c>null</c> for any other source.
    /// </summary>
    public string? SourceUrl { get; private set; }

    /// <summary>For a source that is a Maran panel: the key fingerprint the administrator confirmed against it.</summary>
    public string? ExpectedKeyFingerprint { get; private set; }

    /// <summary>The administrator who created the job, and who it acts as.</summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>When the job was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the engine was started.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the job ended.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Whether the mapping of the job's accounts may still be edited.</summary>
    public bool IsEditable
    {
        get { return State == MigrationJobState.Draft; }
    }

    /// <summary>Whether the engine may start accounts.</summary>
    public bool IsRunning
    {
        get { return State == MigrationJobState.Running; }
    }

    /// <summary>Creates a job in the draft state.</summary>
    /// <param name="id">The job's identifier.</param>
    /// <param name="remoteServerId">The registered server.</param>
    /// <param name="name">The administrator's label.</param>
    /// <param name="concurrency">How many accounts at once.</param>
    /// <param name="bandwidthLimitKib">The bandwidth limit, or <c>null</c>.</param>
    /// <param name="sourceUrl">The pull origin of a Maran source, or <c>null</c>.</param>
    /// <param name="expectedKeyFingerprint">The confirmed key fingerprint of a Maran source, or <c>null</c>.</param>
    /// <param name="createdByUserId">The administrator.</param>
    /// <param name="now">When it is created.</param>
    public MigrationJob(
        Guid id,
        Guid remoteServerId,
        string name,
        int concurrency,
        int? bandwidthLimitKib,
        string? sourceUrl,
        string? expectedKeyFingerprint,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, MigrationJobLimits.MinConcurrency);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(concurrency, MigrationJobLimits.MaxConcurrency);

        Id = id;
        RemoteServerId = remoteServerId;
        Name = ColumnText.Fit(name, NameMaxLength);
        State = MigrationJobState.Draft;
        Concurrency = concurrency;
        BandwidthLimitKib = bandwidthLimitKib;
        SourceUrl = sourceUrl is null ? null : ColumnText.Fit(sourceUrl, SourceUrlMaxLength);
        ExpectedKeyFingerprint = expectedKeyFingerprint;
        CreatedByUserId = createdByUserId;
        CreatedAt = now;
    }

    /// <summary>Required by EF Core only.</summary>
    private MigrationJob()
    {
        Name = string.Empty;
    }

    /// <summary>
    /// Changes the settings that apply while the job runs. Allowed in every state but the ended ones, because the
    /// scheduler re-reads them each tick and a transfer picks the bandwidth up at its next slice.
    /// </summary>
    /// <param name="concurrency">How many accounts at once.</param>
    /// <param name="bandwidthLimitKib">The limit, or <c>null</c> for none.</param>
    /// <returns>Success, or the refusal.</returns>
    public Result ChangeLimits(int concurrency, int? bandwidthLimitKib)
    {
        if (concurrency is < MigrationJobLimits.MinConcurrency or > MigrationJobLimits.MaxConcurrency
            || bandwidthLimitKib is <= 0 or > MigrationJobLimits.MaxBandwidthLimitKib)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationLimitInvalid), ErrorType.Validation));
        }

        if (State is MigrationJobState.Completed or MigrationJobState.Cancelled)
        {
            return Refused();
        }

        Concurrency = concurrency;
        BandwidthLimitKib = bandwidthLimitKib;

        return Result.Ok();
    }

    /// <summary>Starts the engine on a draft job.</summary>
    /// <param name="now">When it starts.</param>
    /// <returns>Success, or the refusal when the job is not a draft.</returns>
    public Result Start(DateTimeOffset now)
    {
        if (State != MigrationJobState.Draft)
        {
            return Refused();
        }

        State = MigrationJobState.Running;
        StartedAt = now;

        return Result.Ok();
    }

    /// <summary>Pauses a running job.</summary>
    /// <returns>Success, or the refusal when the job is not running.</returns>
    public Result Pause()
    {
        if (State != MigrationJobState.Running)
        {
            return Refused();
        }

        State = MigrationJobState.Paused;

        return Result.Ok();
    }

    /// <summary>Resumes a paused job.</summary>
    /// <returns>Success, or the refusal when the job is not paused.</returns>
    public Result Resume()
    {
        if (State != MigrationJobState.Paused)
        {
            return Refused();
        }

        State = MigrationJobState.Running;

        return Result.Ok();
    }

    /// <summary>Cancels a job that has not ended.</summary>
    /// <param name="now">When it ends.</param>
    /// <returns>Success, or the refusal when the job already ended.</returns>
    public Result Cancel(DateTimeOffset now)
    {
        if (State is MigrationJobState.Completed or MigrationJobState.Cancelled)
        {
            return Refused();
        }

        State = MigrationJobState.Cancelled;
        FinishedAt = now;

        return Result.Ok();
    }

    /// <summary>
    /// Ends a running job whose accounts all ended. Idempotent: a job that is not running is left as it is, so the
    /// scheduler can ask on every tick.
    /// </summary>
    /// <param name="now">When it ends.</param>
    public void Complete(DateTimeOffset now)
    {
        if (State != MigrationJobState.Running)
        {
            return;
        }

        State = MigrationJobState.Completed;
        FinishedAt = now;
    }

    /// <summary>The refusal of a transition the job's state does not allow.</summary>
    /// <returns>The failed result.</returns>
    private static Result Refused()
    {
        return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationJobStateInvalid), ErrorType.Conflict));
    }
}
