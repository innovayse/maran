using System.Text.Json.Serialization;

namespace Maran.Modules.Databases.Common;

/// <summary>
/// What a pass did about the rows that needed rewriting: it listed them, or it rewrote them. Exactly
/// one of two shapes, told apart by <c>kind</c>, so the screen never has to choose between two lists
/// by guessing which pass it is reading.
/// </summary>
/// <remarks>
/// <see cref="GrantRepairPreviewDto"/> and <see cref="GrantRepairAppliedDto"/> are the whole set; the
/// derived-type attributes below name them. The two are different claims about the host:
/// "would rewrite" leaves every customer's access as it was, "rewrote" has already changed it, and a
/// reader who cannot tell them apart does not know whether that access has been touched.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GrantRepairPreviewDto), "preview")]
[JsonDerivedType(typeof(GrantRepairAppliedDto), "applied")]
public abstract record GrantRepairWorkDto;
