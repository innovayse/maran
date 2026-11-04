namespace Maran.Modules.Backups.Models;

/// <summary>
/// What one verification of a stored backup artifact concluded: sound, found wanting, or not
/// performed at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three answers, because a verification that could not run is not a verification that failed.</b>
/// A busy agent, an unreachable one or a destination it will not read leaves the backup exactly as it
/// was; only a typed finding about the artifact marks it failed, and only that mails the
/// administrator. Folding the third into the second would tell an administrator that a backup is
/// corrupt because the server was merely busy, and would let retention treat a transient read
/// failure as a damaged copy.
/// </para>
/// <para>
/// Each answer is exactly one member: <see cref="Verified"/> is <c>true</c>; or
/// <see cref="FailureCode"/> is set, which is a finding; or <see cref="NotPerformed"/> is set, which
/// is neither. Never two of them, and the verifier is the only thing that builds one. A carrier the
/// module passes between its own layers and never serialises, so <c>Models/</c>.
/// </para>
/// <para>
/// <b>A finding is announced on the transition into the failed state, not on every finding.</b>
/// <see cref="EnteredFailedState"/> says whether this finding moved the backup into
/// <see cref="Domain.Enums.BackupVerificationState.Failed"/> from another state. A backup already
/// recorded as failed that is found failed again (a repeated "Verify now") has already been reported,
/// and reporting it again per click would let any login that can press the button mail the
/// administrator at will (threat note R-H2).
/// </para>
/// </remarks>
/// <param name="Verified">Whether the artifact was read back and found to match its record.</param>
/// <param name="FailureCode">
/// The module's code for what the agent found wrong with the artifact, recorded on the backup, or
/// <c>null</c> when the artifact was sound or the verification did not run.
/// </param>
/// <param name="NotPerformed">
/// Why the verification could not be performed, as the agent client or this module reported it, or
/// <c>null</c> when it concluded. Nothing was recorded on the backup for it.
/// </param>
/// <param name="EnteredFailedState">
/// <c>true</c> only for a finding that moved a backup which was not recorded as failed into the
/// failed state; <c>false</c> for a sound artifact, a verification that did not run, and a finding on
/// a backup that was already recorded as failed.
/// </param>
public sealed record BackupVerificationOutcome(
    bool Verified,
    string? FailureCode,
    Error? NotPerformed = null,
    bool EnteredFailedState = false);
