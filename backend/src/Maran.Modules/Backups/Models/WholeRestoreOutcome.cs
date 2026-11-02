namespace Maran.Modules.Backups.Models;

/// <summary>
/// A restore that replaced everything it set out to: the account's home is now the archive's home
/// and every database in scope was dropped, re-created and loaded.
/// </summary>
/// <remarks>
/// Carries only the one figure that varies. That the files were restored and that the databases
/// restored equal the databases set out to replace are what makes this case this case, so neither is
/// a field: a field that is true on every instance of its type is a constant wearing the clothes of
/// a measurement.
/// </remarks>
/// <param name="DatabasesRestored">How many databases were dropped, re-created and loaded.</param>
public sealed record WholeRestoreOutcome(uint DatabasesRestored) : RestoreRunOutcome;
