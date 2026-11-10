using Maran.Sdk.Contracts;

namespace Maran.Modules.ServerMigration.Models;

/// <summary>The sections the importers read for one account, and what could not be put in them.</summary>
/// <param name="Sections">The sections, one per module that has something to import.</param>
/// <param name="Findings">What composing them found: conflicts the dry run reports, and warnings.</param>
public sealed record MigrationSectionComposition(
    IReadOnlyList<MigrationSection> Sections,
    IReadOnlyList<MigrationFinding> Findings);
