namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// The part of the service's RFC 7807 failure the panel acts on: the stable code. The service's own
/// sentence is never shown; the panel renders its own localised text for the behaviour the code selects.
/// </summary>
/// <param name="Code">The service's snake_case code, e.g. <c>entitlement_missing</c>.</param>
public sealed record ServiceProblem(string Code);
