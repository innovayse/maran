using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Common;

/// <summary>One feature an owner may give a sub-user, with its name in the caller's language.</summary>
/// <param name="Feature">The feature, serialized as its camel-case name; what a grant sends back.</param>
/// <param name="FeatureDisplayName">What the feature is called on the screen, already localized by the panel.</param>
public sealed record GrantableFeatureDto(AccountFeature Feature, string FeatureDisplayName);
