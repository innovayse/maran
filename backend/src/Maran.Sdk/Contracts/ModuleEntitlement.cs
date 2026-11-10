namespace Maran.Sdk.Contracts;

/// <summary>
/// The licence window's answer for one module: its id, the verdict, and when the cover ends. It is the
/// narrowest value that answers the question and carries nothing else on purpose: no licence text, no
/// signature, no machine id, no lease body and no key may ever become a member, because every module
/// and every filter can read it (docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md §1).
/// A reflection test in <c>Maran.Sdk.Tests</c> lists the members so an addition fails the build.
/// </summary>
/// <param name="ModuleId">The module's <see cref="Manifest.Id"/>.</param>
/// <param name="Verdict">Whether the module is covered right now.</param>
/// <param name="ValidUntil">
/// The effective end of cover: the licence's expiry, or the lease's end when a lease applies and ends
/// first. For a <see cref="EntitlementVerdict.Lapsed"/> module, when the cover ended. Null when there
/// is no cover to date (<see cref="EntitlementVerdict.NotCovered"/>).
/// </param>
public sealed record ModuleEntitlement(string ModuleId, EntitlementVerdict Verdict, DateTimeOffset? ValidUntil);
