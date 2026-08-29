namespace Maran.Modules.Accounts.Controllers.Requests;

/// <summary>HTTP request body for <c>POST api/v1/accounts</c>.</summary>
/// <param name="Name">The account's unique, Linux-username-safe short name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
public sealed record CreateAccountRequest(string Name, string PrimaryDomain, Guid PlanId);
