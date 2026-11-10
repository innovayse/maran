namespace Maran.Modules.ServerMigration.Jobs;

/// <summary>
/// The message that runs one import's job (issue #127). It carries an id and nothing else: no token, no
/// password and no key is ever in a Wolverine envelope, because an envelope's body is persisted.
/// </summary>
/// <remarks>
/// Who the job acts as is read from the import row (the administrator who staged it) and re-checked against the
/// user table on every run, never taken from this message.
/// </remarks>
/// <param name="ImportId">The import to run.</param>
public sealed record AccountImportRequested(Guid ImportId);
