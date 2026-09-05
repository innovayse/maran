using Maran.Modules.Cron.Services;
namespace Maran.Modules.Cron.Common;

/// <summary>One environment assignment in the agent-managed region of an account's crontab.</summary>
/// <remarks>
/// One type for reading and for writing, because the set is REPLACED whole rather than merged: what
/// a caller sends is exactly what the crontab will hold, so the two directions describe the same
/// thing.
///
/// Assignments the account or the host wrote outside the agent's own region are neither reported
/// here nor overwritten by a write.
/// </remarks>
/// <param name="Name">
/// The variable's name: an uppercase letter or underscore followed by uppercase letters, digits and
/// underscores. <c>MAILTO</c> and <c>SHELL</c> are refused — the agent writes both itself, and a
/// customer who could set the first would have an outbound mail relay while one who could set the
/// second would choose the interpreter every one of their entries runs under.
/// </param>
/// <param name="Value">
/// The value, written verbatim into a <c>NAME=value</c> line of the crontab. Like the command, it is
/// the customer's own and may carry a credential, so it is shown back to them and appears in no log
/// line and in no audit entry — the journal records the NAMES that changed and never the values
/// (<see cref="CronAuditJournal"/>).
/// </param>
public sealed record CronEnvironmentVariableDto(string Name, string Value);
