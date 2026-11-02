namespace Maran.Modules.Accounts.Common;

/// <summary>
/// The count could not be taken, because the agent that holds the truth did not answer
/// (<c>state</c> is <c>unavailable</c>). It carries no number.
/// </summary>
public sealed record UnavailableResourceCountDto : ResourceCountDto;
