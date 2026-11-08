using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// This module's implementation of <see cref="IAccountBackupService"/>: the final backup an account
/// gets before it is destroyed (spec §12).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is <see cref="SafetyBackupService"/> with two differences, and both of them are about the row outliving its
/// account.</b> The row's kind is <see cref="BackupKind.PreDeletion"/>, which is what exempts it
/// from this module's own account cascade and from retention; and the answer is a
/// <see cref="Result{T}"/> rather than a recorded outcome the caller reads at leisure, because the
/// caller has to be able to refuse the deletion on it.
/// </para>
/// <para>
/// <b>Why a failed run answers a FAILURE here and a completed row on the customer's own create
/// path.</b> <c>CreateBackupCommandHandler</c> deliberately answers success carrying a
/// <see cref="BackupStatus.Failed"/> row, because a screen has to show the failure either way and an
/// error would leave the caller holding a code and no id. Here the caller is not a screen: it is a
/// deletion deciding whether to proceed, and the only thing it can do with "the row says Failed" is
/// what a failed <see cref="Result{T}"/> already says. Folding it into the result is what makes the
/// refusal impossible to forget at the call site.
/// </para>
/// <para>
/// <b>The row is written before the agent is asked and the failure is recorded, not discarded.</b>
/// A refused deletion leaves a <see cref="BackupStatus.Failed"/> <see cref="BackupKind.PreDeletion"/>
/// row naming the account and the code — which is the record an operator needs in order to fix
/// whatever refused and delete the account afterwards. A run that left nothing behind would present
/// the operator with a deletion that fails for a reason nothing in the panel states.
/// </para>
/// <para>
/// <b>No panel task is opened, and that is deliberate.</b> The deletion has already opened one
/// (<c>TaskKinds.AccountDeletion</c>) and reports this step onto it; a second task for a step of the
/// first would give an operator two rows for one operation, which can disagree. The run's progress
/// is therefore reported onto the deletion's task, whose id the caller does not pass — so this step
/// contributes its own stage line through the deletion handler instead, and the runner is given
/// <see cref="Guid.Empty"/>, which <see cref="ITaskRecorder"/> defines as "there is no task".
/// </para>
/// </remarks>
public sealed class AccountBackupService : IAccountBackupService
{
    /// <summary>The one path that takes a safety backup; shared with the restore's pre-restore copy.</summary>
    private readonly SafetyBackupService _safetyBackups;

    /// <summary>Creates the service.</summary>
    /// <param name="safetyBackups">The one path that takes a safety backup.</param>
    public AccountBackupService(SafetyBackupService safetyBackups)
    {
        _safetyBackups = safetyBackups;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> TakeFinalBackupAsync(
        Guid accountId,
        string accountName,
        CancellationToken cancellationToken)
    {
        // The caller has no address to record — a deletion is journalled by the Accounts module
        // under its own action — so what the journal adds is the fact that the FINAL backup
        // specifically was attempted and how it ended, against the account being destroyed: the
        // one name that is left to search for once the deletion finishes. The runner is given
        // Guid.Empty: the deletion's own task is the one an operator watches.
        return await _safetyBackups.TakeAsync(
            BackupKind.PreDeletion,
            accountId,
            accountName,
            AccountAuditActions.FinalBackupTaken,
            nameof(ErrorMessages.FinalBackupFailed),
            Guid.Empty,
            string.Empty,
            string.Empty,
            cancellationToken);
    }
}
