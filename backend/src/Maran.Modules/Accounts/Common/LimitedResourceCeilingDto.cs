namespace Maran.Modules.Accounts.Common;

/// <summary>The plan caps the resource (<c>kind</c> is <c>limited</c>).</summary>
/// <param name="Maximum">The cap, in the unit of the resource the ceiling is for; always positive.</param>
public sealed record LimitedResourceCeilingDto(int Maximum) : ResourceCeilingDto;
