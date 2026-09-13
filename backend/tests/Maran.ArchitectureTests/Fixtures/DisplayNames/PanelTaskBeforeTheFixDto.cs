namespace Maran.ArchitectureTests.Fixtures.DisplayNames;

/// <summary>
/// The tasks screen as it shipped: a task's kind on the wire as a machine name and nothing else, so
/// <c>/tasks</c> printed <c>BackupCreate</c> in the operation column of a Russian panel.
/// </summary>
/// <remarks>
/// Reconstructed to the members that carry the defect rather than to all twelve of the real type's:
/// what made it wrong was a <see cref="string"/> identifier with no name beside it, and the rest of
/// the row was innocent. The fix — <c>KindDisplayName</c>, resolved by <c>TaskKindDisplayNames</c> —
/// is the shape <see cref="PanelTaskAfterTheFixDto"/> holds.
/// </remarks>
/// <param name="Id">The task's identity.</param>
/// <param name="Kind">The operation, as the machine-stable name the row records.</param>
/// <param name="Subject">What it acts on.</param>
public sealed record PanelTaskBeforeTheFixDto(Guid Id, string Kind, string Subject);
