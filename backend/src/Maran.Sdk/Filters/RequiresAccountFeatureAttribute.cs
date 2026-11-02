using Maran.Sdk.Contracts;

namespace Maran.Sdk.Filters;

/// <summary>
/// Marks a controller (or an action) as part of a plan feature: a caller acting for an account whose
/// plan lacks <see cref="Feature"/> is refused before the action runs, whatever the menu shows.
/// One attribute line on the class is the whole opt-in.
/// </summary>
/// <remarks>
/// Built on <see cref="TypeFilterAttribute"/> so the filter is constructed per request with its
/// dependencies from the container and the feature as its argument; see
/// <see cref="RequiresAccountFeatureFilter"/> for who is refused and with what.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresAccountFeatureAttribute : TypeFilterAttribute
{
    /// <summary>The feature the caller's plan must include.</summary>
    public AccountFeature Feature { get; }

    /// <summary>Creates the attribute for one feature.</summary>
    /// <param name="feature">The feature the plan must include.</param>
    public RequiresAccountFeatureAttribute(AccountFeature feature)
        : base(typeof(RequiresAccountFeatureFilter))
    {
        Feature = feature;
        Arguments = new object[] { feature };
    }
}
