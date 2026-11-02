namespace Maran.Modules.Backups.Models;

/// <summary>
/// A restore the agent stated an outcome for and that did NOT replace everything: the account is
/// half-replaced, and these are the agent's own figures for how far it got.
/// </summary>
/// <remarks>
/// Its <see cref="Failure"/> is a whole <see cref="Error"/> and not a bare code, so that its KIND
/// cannot be re-invented downstream: the handler once built <c>Error.Of(code, ErrorType.Failure)</c>
/// from a loose string, which answered HTTP 500 over an artifact the agent had refused as unusable —
/// an <see cref="ErrorType.Validation"/> the agent client had already decided and this carrier threw
/// away (rules/csharp.md "A type that exists to make two facts inseparable is stating a rule").
/// </remarks>
/// <param name="FilesRestored">Whether the account's home is now the archive's home.</param>
/// <param name="DatabasesRestored">How many databases were dropped, re-created and loaded.</param>
/// <param name="DatabasesTotal">How many the restore set out to replace, after the allowed list.</param>
/// <param name="Failure">What went wrong: the machine-stable code AND its kind.</param>
public sealed record PartialRestoreOutcome(
    bool FilesRestored,
    uint DatabasesRestored,
    uint DatabasesTotal,
    Error Failure) : RestoreRunOutcome;
