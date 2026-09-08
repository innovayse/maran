namespace Maran.Agent.Client.Services.BackupService;

/// <summary>One entry of a listing: an artifact that exists, and either its description or why it has none.</summary>
/// <remarks>
/// <para>
/// The listing reports an artifact it cannot describe rather than omitting it. An entry nobody lists
/// is an entry retention will never prune, and a backup directory silently accumulating unprunable
/// files is the failure this whole area exists to refuse.
/// </para>
/// <para>
/// Exactly one of <see cref="Readable"/> and <see cref="Unreadable"/> is non-null, and every one of
/// these is built by <c>AgentBackupClient.ListAsync</c>, which is the only thing that constructs
/// them. The size, creation time and digest are deliberately NOT repeated as flat members beside the
/// two arms even though the wire carries them that way: those wire fields are zero and empty on an
/// unreadable entry, and a caller reading a zero cannot tell "the artifact is empty" from "nobody
/// could say". The arm is the authority here, as the proto says it is there.
/// </para>
/// </remarks>
/// <param name="BackupId">Identifier of the backup, as passed to create.</param>
/// <param name="Readable">The description, when the artifact had a trustworthy one; null otherwise.</param>
/// <param name="Unreadable">Why there is no description; null when there is one.</param>
public sealed record AgentBackupSummary(
    string BackupId,
    AgentReadableBackup? Readable,
    AgentBackupUnreadableReason? Unreadable);
