namespace Maran.Modules.ServerMigration.Common;

/// <summary>One finding of a job's dry run, about one source account, in the caller's language.</summary>
/// <param name="SourceAccountName">The account.</param>
/// <param name="ModuleId">The module that found it.</param>
/// <param name="Severity">Info, Warning or Conflict.</param>
/// <param name="Code">The machine-stable code.</param>
/// <param name="Message">The code as a sentence in the caller's language.</param>
/// <param name="Subject">What it is about, as the administrator knows it.</param>
public sealed record MigrationAccountFindingDto(
    string SourceAccountName,
    string ModuleId,
    string Severity,
    string Code,
    string Message,
    string Subject);
