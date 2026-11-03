namespace Maran.Modules.Accounts.Queries.GetMyReseller;

/// <summary>
/// Reads the reseller the CALLER is. Carries no reseller id: the answer is always "the reseller the
/// caller's own token names", read off <see cref="SharedKernel.Callers.ResellerCaller.ResellerId"/>
/// inside <see cref="GetMyResellerQueryHandler"/> — a parameter here would be a way to ask for another
/// reseller, and there is no such parameter to forge.
/// </summary>
public sealed record GetMyResellerQuery;
