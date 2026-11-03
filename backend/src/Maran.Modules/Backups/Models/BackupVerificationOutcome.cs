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
public sealed record BackupVerificationOutcome(
    bool Verified,
    string? FailureCode,
    Error? NotPerformed = null);
