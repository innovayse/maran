namespace Maran.Modules.Backups.Models;

/// <summary>
/// A restore run the agent stated nothing usable about: a refusal, a dropped stream, or a terminal
/// event that promised an outcome and carried none.
/// </summary>
/// <remarks>
/// It carries no figures, and that is the point. What the account holds now is unknown, so a count
/// of zero would read as "nothing was replaced" — a measurement nobody made. The code is what the
/// caller is meant to read, and the handler publishes no counts for this case.
/// </remarks>
/// <param name="Failure">The machine-stable code naming what went wrong, and its kind.</param>
public sealed record UnmeasuredRestoreOutcome(Error Failure) : RestoreRunOutcome;
