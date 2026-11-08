using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.BackupService;

/// <summary>The agent's own root-only backup directory on this server.</summary>
/// <remarks>
/// <para>
/// The local root is the agent's own, and the agent refuses a destination that names a path or an S3
/// field rather than ignoring it, so the only thing this type can carry is the one thing the agent
/// accepts here: the key a backup archive is sealed with.
/// </para>
/// <para>
/// <b><see cref="ArchiveKey"/> is a SECRET and is held in <see cref="SensitiveString"/>, for the
/// reason <see cref="AgentS3BackupDestination"/> holds its credentials so.</b> This type is a
/// <c>record</c>, whose compiler-generated <see cref="object.ToString"/> prints every property, so a
/// bare <c>string</c> would put the key in the first log line that formatted a destination.
/// </para>
/// <para>
/// <b>Whether it is sent depends on the call.</b> The client puts it on the wire for creating,
/// restoring and verifying a backup — the three calls that seal or open an archive — and for none
/// of the others, which never open one. A key present on a destination used to CREATE a backup seals
/// the archive, so the panel hands creation a destination that carries the key only while the
/// destination is set to encrypt (<c>BackupDestinationMapper.ForNewBackup</c>), and hands every
/// reading call one that carries the key whenever the destination has one, so a backup sealed
/// before encryption was switched off stays readable.
/// </para>
/// </remarks>
/// <param name="ArchiveKey">
/// The 256-bit archive key as 64 lowercase hexadecimal characters, or <c>null</c> for a plain
/// archive.
/// </param>
public sealed record AgentLocalBackupDestination(SensitiveString? ArchiveKey = null) : AgentBackupDestination;
