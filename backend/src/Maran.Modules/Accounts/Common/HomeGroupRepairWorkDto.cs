using System.Text.Json.Serialization;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// What a pass did about the homes that needed re-grouping: it listed them, or it re-grouped them.
/// Exactly one of two shapes, told apart by <c>kind</c>, so the screen never has to choose between
/// two lists by guessing which pass it is reading.
/// </summary>
/// <remarks>
/// <see cref="HomeGroupRepairPreviewDto"/> and <see cref="HomeGroupRepairAppliedDto"/> are the whole
/// set; the derived-type attributes below name them. The two are different claims about the host:
/// "would re-group" leaves every home as it was, "re-grouped" has already changed it, and a reader who
/// cannot tell them apart does not know whether the customers' homes have been touched. The case is
/// stated by the caller that ran the pass and carried in the type, never inferred from whether the
/// list is empty: a repairing pass over a host with nothing to repair produces the same empty list a
/// report-only pass does. It sits inside the report rather than being the report, so it is always
/// serialized as a member of a declared type, which is where the <c>kind</c> discriminator is written.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HomeGroupRepairPreviewDto), "preview")]
[JsonDerivedType(typeof(HomeGroupRepairAppliedDto), "applied")]
public abstract record HomeGroupRepairWorkDto;
