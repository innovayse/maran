using Maran.Agent.Client.Services.BackupService;

namespace Maran.Modules.Backups.Models;

/// <summary>
/// The destination one operation will use: the row's identity, and the shape an agent call names.
/// </summary>
/// <remarks>
/// <para>
/// A carrier the module passes between its own layers, never serialised — so <c>Models/</c> rather
/// than <c>Common/</c> (rules/csharp.md, the fifth question).
/// </para>
/// <para>
/// <b>The two halves travel together because they must not disagree.</b> The identity is what a
/// <c>Backup</c> row records as the place its artifact went, and the wire shape is what the agent is
/// actually told; a handler that resolved one and rebuilt the other would be free to stamp a row
/// with a destination the call never used, and nothing downstream could tell.
/// </para>
/// </remarks>
/// <param name="Id">The recorded destination's identity, stamped onto the backup row.</param>
/// <param name="Agent">
/// The destination an agent call that READS a backup (restore, verify, list, delete) carries: it holds
/// the archive key whenever the destination has one, so an archive sealed earlier can still be opened.
/// </param>
/// <param name="AgentForNewBackup">
/// The destination a call that CREATES a backup carries: it holds the archive key only while the
/// destination is set to encrypt, because on a creation the key is the instruction to seal. The two
/// are different values on purpose and a creation that used <paramref name="Agent"/> would seal archives
/// of a destination whose encryption was switched off.
/// </param>
public sealed record ResolvedBackupDestination(
    Guid Id,
    AgentBackupDestination Agent,
    AgentBackupDestination AgentForNewBackup);
