namespace Maran.Packaging.Models;

/// <summary>One module version range the marketplace has withdrawn.</summary>
/// <param name="ModuleId">The withdrawn module's id.</param>
/// <param name="Range">
/// The withdrawn versions in the grammar of <c>VersionRange</c>. A minimum-version floor is the same
/// thing written as a range, <c>&lt;2.1.0</c>: one meaning, one field.
/// </param>
/// <param name="Reason">A short machine token saying why, such as <c>key-compromise</c> or <c>vulnerability</c>.</param>
public sealed record RevocationEntry(string ModuleId, string Range, string Reason);
