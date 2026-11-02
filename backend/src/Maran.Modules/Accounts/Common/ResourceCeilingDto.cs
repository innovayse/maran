using System.Text.Json.Serialization;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One runtime ceiling of a plan on the wire, in both directions: what a plan states and what an
/// administrator sends to define or edit one. Exactly one of two shapes, told apart by <c>kind</c>,
/// so a figure exists only where there is a cap and "no ceiling" is never read off a zero.
/// </summary>
/// <remarks>
/// The closed set is stated by the <c>JsonDerivedType</c> attributes: <see cref="LimitedResourceCeilingDto"/> and
/// <see cref="UnlimitedResourceCeilingDto"/> are the whole set. The stored ceiling has the same two
/// cases (a nullable figure, null when uncapped), and <see cref="Mappers.RuntimeLimitsMapper"/> is
/// the one place the two are restated into each other.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LimitedResourceCeilingDto), "limited")]
[JsonDerivedType(typeof(UnlimitedResourceCeilingDto), "unlimited")]
public abstract record ResourceCeilingDto;
