using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// One runtime ceiling of a plan on the wire, in both directions: what a plan states and what an
/// administrator sends to define or edit one. Both halves state their own case, so "no ceiling" is
/// never read off a zero.
/// </summary>
/// <param name="Kind">Whether the plan caps this resource at all.</param>
/// <param name="Maximum">The cap when <paramref name="Kind"/> is <c>Limited</c>; zero otherwise.</param>
public sealed record ResourceCeilingDto(ResourceLimitKind Kind, int Maximum);
