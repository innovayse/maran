using System.Text.Json.Serialization;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One runtime ceiling of a plan on the wire, in both directions: what a plan states and what an
/// administrator sends to define or edit one. Exactly one of two shapes, told apart by <c>kind</c>,
/// so a figure exists only where there is a cap and "no ceiling" is never read off a zero.
/// </summary>
/// <remarks>
/// The constructor is private to this assembly, so <see cref="LimitedResourceCeilingDto"/> and
/// <see cref="UnlimitedResourceCeilingDto"/> are the whole set. The panel's stored ceiling still
/// keeps a kind beside a figure that is zero when uncapped (a persisted shape), and
/// <see cref="Mappers.RuntimeLimitsMapper"/> is the one place that pair becomes this union.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LimitedResourceCeilingDto), "limited")]
[JsonDerivedType(typeof(UnlimitedResourceCeilingDto), "unlimited")]
public abstract record ResourceCeilingDto
{
    /// <summary>Closes the hierarchy to the two shapes this assembly declares.</summary>
    private protected ResourceCeilingDto()
    {
    }
}
