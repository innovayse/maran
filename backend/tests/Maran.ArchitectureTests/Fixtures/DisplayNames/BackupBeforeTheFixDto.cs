using Maran.Modules.Backups.Domain.Enums;

namespace Maran.ArchitectureTests.Fixtures.DisplayNames;

/// <summary>
/// The backups table as it shipped: the failure carried only its machine code, so a status cell
/// read <c>AgentSystemFailure</c> in the middle of a Russian sentence.
/// </summary>
/// <remarks>
/// <para>
/// Both defective members are here, and they fail for different reasons — which is the whole
/// argument of the law in one type. <c>FailureCode</c> is a <see cref="string"/> drawn from two
/// closed sets that live on the other side of the wire (every backup event kind, every proto error
/// code), and no bundle can word them ahead of time, so the backend must: the fix was
/// <c>FailureDisplayName</c> beside it. <c>Status</c> is an enum of three values the panel itself
/// closes, and the real <c>BackupDto</c> is excused for it in
/// <see cref="Maran.ArchitectureTests.DisplayNameExemptions"/> against a locale key set the law
/// resolves in all three languages. A fixture carries no exemption, so here the law flags both.
/// </para>
/// </remarks>
/// <param name="Id">The backup's identity.</param>
/// <param name="Status">How far the run got.</param>
/// <param name="FailureCode">The machine-stable code of the failure, or empty when nothing failed.</param>
public sealed record BackupBeforeTheFixDto(Guid Id, BackupStatus Status, string FailureCode);
