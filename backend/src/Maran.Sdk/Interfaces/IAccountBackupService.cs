namespace Maran.Sdk.Interfaces;

/// <summary>
/// Takes the one backup an account gets before it is destroyed, so the panel's most destructive
/// operation is not also its only irreversible one (spec §12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an act-window and not a message.</b> Every other cross-module need in this panel is either
/// a published message (<c>SendMailRequested</c>) or a read-only window
/// (<see cref="IAccountDirectory"/>), and this is neither. It is an ACT the caller must be able to
/// wait for and then REFUSE ON: a deletion that carried on because a message had been handed to a
/// queue would be a deletion taken on the strength of nothing having been observed yet. A published
/// message cannot give an answer, so it cannot be the shape of a step whose answer decides whether
/// the next step runs.
/// </para>
/// <para>
/// <b>Only the act crosses; the credential does not.</b> The object store's key, the operator's
/// local root and the destination row stay inside the Backups module, exactly as
/// rules/architecture.md requires of a facility that holds a secret. What the caller hands over is
/// an account and what it gets back is an identifier or a code — a surface no consumer can read a
/// storage location out of.
/// </para>
/// <para>
/// <b>It is injected as <c>IAccountBackupService?</c>, and the nullability is the point.</b> A panel
/// composed without the Backups module has no backup to take, and the caller must be able to tell
/// that state apart from "a backup was taken". With a message it could not: an event with no
/// subscriber raises nothing and reads exactly like success — the same observation that let an
/// account deletion report COMPLETED over two modules that had released nothing
/// (<see cref="IAccountResidueAuditor"/>'s own remarks record it). A null reference is a fact the
/// code branches on and a test can set, so "there was no module" and "the backup succeeded" can
/// never again be the same reading.
/// </para>
/// <para>
/// <b>What this does NOT promise.</b> The backup covers what an account backup covers — the home
/// directory and the account's databases — and nothing else: not the vhosts, not the certificates,
/// not the crontab, not the firewall rules. Restoring from it re-creates a customer's files and
/// data inside an account somebody has to create again first. A caller that reads this as "the
/// deletion is undoable" has been misled, which is why the limit is stated here rather than left to
/// be discovered by the operator who needed it.
/// </para>
/// </remarks>
public interface IAccountBackupService
{
    /// <summary>Takes the final backup of an account that is about to be deleted.</summary>
    /// <param name="accountId">The account being deleted.</param>
    /// <param name="accountName">
    /// The account's system user name. Passed in rather than looked up, because the one caller is
    /// the module that owns the accounts table and has the row in hand — and because a lookup here
    /// would be a second, tenant-scoped reading of an account the caller has already authorised the
    /// destruction of.
    /// </param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>
    /// The identifier of the recorded backup, or a typed failure. A failure means no usable archive
    /// exists, and the caller is expected to abandon the deletion on it: the alternative is
    /// destroying the last copy of a customer's data on the strength of a run that did not finish.
    /// </returns>
    /// <remarks>
    /// It must be called BEFORE anything is released, because a backup taken after the databases
    /// have been dropped is a backup of nothing. The recorded backup deliberately outlives the
    /// account it names — that is what makes it a final backup rather than a step in the deletion.
    /// </remarks>
    Task<Result<Guid>> TakeFinalBackupAsync(Guid accountId, string accountName, CancellationToken cancellationToken);
}
