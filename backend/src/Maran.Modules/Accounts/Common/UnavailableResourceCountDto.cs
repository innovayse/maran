namespace Maran.Modules.Accounts.Common;

/// <summary>
/// The count was not taken (<c>state</c> is <c>unavailable</c>): the agent that holds the truth did
/// not answer, or the caller is a reseller, whom no module that owns a resource shows any row. It
/// carries no number, so a screen cannot print zero for either.
/// </summary>
public sealed record UnavailableResourceCountDto : ResourceCountDto;
