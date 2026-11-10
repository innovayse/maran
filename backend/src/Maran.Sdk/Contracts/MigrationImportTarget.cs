namespace Maran.Sdk.Contracts;

/// <summary>The account an importer imports into.</summary>
/// <param name="AccountId">
/// The account's id on this server, or <c>null</c> until the Accounts importer has created it. Every importer
/// but the Accounts one refuses a target without one; the orchestrator runs the Accounts importer first
/// (<see cref="Interfaces.IAccountMigrationImporter.Order"/>) and hands the id it created to the rest.
/// </param>
/// <param name="AccountName">The account's system user name.</param>
/// <param name="Choices">What the administrator decided.</param>
public sealed record MigrationImportTarget(Guid? AccountId, string AccountName, MigrationImportChoices Choices);
