using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Common;

/// <summary>One feature an owner may give a sub-user, with its name in the caller's language.</summary>
/// <param name="Id">The feature, serialized as its camel-case name.</param>
/// <param name="Label">What the feature is called on the screen, already localized by the panel.</param>
public sealed record GrantableFeatureDto(AccountFeature Id, string Label);
