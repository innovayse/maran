namespace Maran.Sdk.Contracts;

/// <summary>What the administrator decides about an import, which a package cannot say for itself.</summary>
/// <param name="PlanId">The plan the account is created against on this server.</param>
/// <param name="ResellerId">The reseller that is to own the account, or <c>null</c> for an account of the administrator's own.</param>
public sealed record MigrationImportChoices(Guid PlanId, Guid? ResellerId);
