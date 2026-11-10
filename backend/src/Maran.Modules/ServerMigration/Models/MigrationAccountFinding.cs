using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Models;

/// <summary>One finding of the dry run, with the source account it is about.</summary>
/// <param name="SourceAccountName">The account.</param>
/// <param name="Finding">The finding.</param>
public sealed record MigrationAccountFinding(string SourceAccountName, MigrationFinding Finding);
