using Maran.Agent.Client.Interfaces;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Persistence;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Asks the agent to read one stored backup artifact back, records what it found on the backup, and
/// journals it — the one path a verification takes, after a run and on request alike (issue #134).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is shared so that the two callers cannot disagree about what a verification means.</b> The
/// endpoint a person clicks and the run that has just finished both ask this type, and an artifact
/// found damaged is recorded, journalled and reported the same way whoever asked.
/// </para>
/// <para>
/// <b>Only a finding about the artifact marks a backup failed.</b> The answer the agent gives is one
/// of three. A success marks it verified. A refusal that says the artifact is corrupt, changed,
/// inconsistent or missing marks it failed, with this module's code for that finding. Every other
/// refusal — the account's backup lock held, the agent saturated or unreachable, a destination the
/// agent will not read — is a verification that did not run: nothing is recorded, nothing is
/// reported as a finding, and the caller is handed the reason. The distinction is
/// <see cref="BackupVerificationAgentErrorTranslator"/>'s.
/// </para>
/// <para>
/// <b>It never touches the run's own status.</b> The run produced an artifact; whether the artifact
/// still reads back is a second fact with its own date and code, and conflating the two would turn a
/// transient read failure into a failed backup that retention then treats as disposable.
/// </para>
/// <para>
/// <b>What a verified backup is, and is not.</b> At the moment of the read the artifact hashed to what
/// its record says and its contents were consistent with it. It is not a promise that the backup will
/// restore, that the SQL loads, or that the artifact is unchanged tomorrow, and nothing this type
/// records or journals says otherwise.
/// </para>
/// <para>
/// The backup passed in must be tracked by this module's context in the current scope — the handler
/// that loaded it through that context, or the run that just wrote it — because the state is recorded
/// on the entity and saved through the same context.
/// </para>
/// </remarks>
public sealed class BackupVerifier
{
    /// <summary>The Backups module's database context, through which the verification state is saved.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The agent, which reads the artifact back.</summary>
    private readonly IAgentBackupClient _agent;

    /// <summary>Resolves the destination the artifact was written to.</summary>
    private readonly BackupDestinationResolver _destinations;

    /// <summary>This module's audit journal.</summary>
    private readonly BackupAuditJournal _journal;

    /// <summary>The injected time source used to stamp the verification.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the verifier.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="agent">The agent client that reads the artifact back.</param>
    /// <param name="destinations">Resolves the destination the artifact was written to.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source.</param>
    public BackupVerifier(
        BackupsDbContext dbContext,
        IAgentBackupClient agent,
        BackupDestinationResolver destinations,
        BackupAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _agent = agent;
        _destinations = destinations;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Verifies one backup on the panel's own initiative, with no signed-in caller.</summary>
    /// <param name="backup">The backup to verify, tracked by this module's context.</param>
    /// <param name="accountUsername">The system user name of the account the backup belongs to.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    /// <returns>What the verification concluded, or why it could not be performed.</returns>
    /// <remarks>
    /// Journalled under the module's system actor: nobody asked, and the line has to say the panel did.
    /// </remarks>
    public async Task<BackupVerificationOutcome> VerifyAsync(
        Backup backup,
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var outcome = await ConcludeAsync(backup, accountUsername, cancellationToken);

        await _journal.RecordSystemAsync(
            ActionOf(outcome), accountUsername, outcome.Verified, cancellationToken);

        return outcome;
    }

    /// <summary>Verifies one backup because a signed-in caller asked for it.</summary>
    /// <param name="backup">The backup to verify, tracked by this module's context.</param>
    /// <param name="accountUsername">The system user name of the account the backup belongs to.</param>
    /// <param name="ipAddress">The caller's address, recorded in the audit journal.</param>
    /// <param name="userAgent">The caller's user agent, recorded in the audit journal.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    /// <returns>What the verification concluded, or why it could not be performed.</returns>
    public async Task<BackupVerificationOutcome> VerifyAsync(
        Backup backup,
        string accountUsername,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var outcome = await ConcludeAsync(backup, accountUsername, cancellationToken);

        if (outcome.Verified)
        {
            await _journal.RecordSuccessAsync(
                ActionOf(outcome), accountUsername, ipAddress, userAgent, cancellationToken);
        }
        else
        {
            await _journal.RecordFailureAsync(
                ActionOf(outcome), accountUsername, ipAddress, userAgent, cancellationToken);
        }

        return outcome;
    }

    /// <summary>Asks the agent and records a conclusion on the backup; the journal is the caller's.</summary>
    /// <param name="backup">The backup to verify.</param>
    /// <param name="accountUsername">The account's system user name, which the agent finds the artifact under.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    /// <returns>The outcome, with nothing recorded when it says the verification was not performed.</returns>
    private async Task<BackupVerificationOutcome> ConcludeAsync(
        Backup backup,
        string accountUsername,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);

        // The destination the artifact was written to, from the row, exactly as a deletion and a
        // restore resolve it: verifying through the current default would read a place the artifact
        // never was and report the artifact missing.
        var destination = await _destinations.ResolveAsync(backup.DestinationId, cancellationToken);
        if (!destination.IsSuccess)
        {
            return new BackupVerificationOutcome(Verified: false, FailureCode: null, destination.Error);
        }

        var answer = await _agent.VerifyBackupAsync(
            accountUsername, backup.Id.ToString(), destination.Value!.Agent, cancellationToken);

        if (answer.IsSuccess)
        {
            backup.MarkVerified(_clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new BackupVerificationOutcome(Verified: true, FailureCode: null);
        }

        var finding = BackupVerificationAgentErrorTranslator.ToFindingCode(answer.Error!);
        if (finding is null)
        {
            return new BackupVerificationOutcome(
                Verified: false,
                FailureCode: null,
                BackupVerificationAgentErrorTranslator.ToNotPerformed(answer.Error!));
        }

        backup.MarkVerificationFailed(_clock.UtcNow, finding);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new BackupVerificationOutcome(Verified: false, finding);
    }

    /// <summary>Names the audit action an outcome is journalled under.</summary>
    /// <param name="outcome">What the verification concluded.</param>
    /// <returns>
    /// <see cref="BackupAuditActions.BackupVerificationFailed"/> for a finding and
    /// <see cref="BackupAuditActions.BackupVerified"/> for everything else, a verification that could
    /// not run included — which is then recorded as failed, so it does not read as a finding.
    /// </returns>
    private static string ActionOf(BackupVerificationOutcome outcome)
    {
        return outcome.FailureCode is null
            ? BackupAuditActions.BackupVerified
            : BackupAuditActions.BackupVerificationFailed;
    }
}
