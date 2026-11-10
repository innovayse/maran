namespace Maran.Modules.ServerMigration.Common;

/// <summary>One finding of the dry run, with its sentence already in the caller's language.</summary>
/// <param name="ModuleId">The module that found it.</param>
/// <param name="Severity">How much it matters: <c>Info</c>, <c>Warning</c> or <c>Conflict</c>, the closed names of the finding severity.</param>
/// <param name="Code">The machine-stable code.</param>
/// <param name="Message">The sentence in the caller's language.</param>
/// <param name="Subject">What it is about.</param>
public sealed record MigrationFindingDto(
    string ModuleId,
    string Severity,
    string Code,
    string Message,
    string Subject);
