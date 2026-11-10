namespace Maran.Sdk.Contracts;

/// <summary>What one importer did.</summary>
/// <param name="CreatedAccountId">The id of the account the importer created; set only by the Accounts importer.</param>
/// <param name="Secrets">The credentials generated for imported resources, to be shown once.</param>
public sealed record MigrationSectionOutcome(Guid? CreatedAccountId, IReadOnlyList<MigrationIssuedSecret> Secrets);
