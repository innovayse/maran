using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Models;

/// <summary>The answer of one walk along the audit chain.</summary>
/// <param name="Intact"><c>true</c> when every sealed row checked out.</param>
/// <param name="RowsChecked">How many sealed rows were examined, the broken one included.</param>
/// <param name="FirstBrokenChainIndex">
/// The chain index at which the walk stopped; <c>null</c> when the chain is intact. A break at the very
/// first index reads the same as a changed chain key, and nothing here can tell them apart.
/// </param>
/// <param name="Break">What was wrong at that index; <see cref="AuditChainBreak.None"/> when intact.</param>
public sealed record AuditChainVerdict(bool Intact, long RowsChecked, long? FirstBrokenChainIndex, AuditChainBreak Break);
