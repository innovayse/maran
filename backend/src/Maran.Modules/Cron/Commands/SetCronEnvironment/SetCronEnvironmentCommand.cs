using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Services;

namespace Maran.Modules.Cron.Commands.SetCronEnvironment;

/// <summary>
/// Replaces the agent-managed environment assignments in an account's crontab, whole.
/// </summary>
/// <remarks>
/// A REPLACEMENT rather than a merge, matching the agent's own contract: a name absent from
/// <paramref name="Variables"/> is removed, and an empty list is how every managed assignment is
/// cleared — which is a request to be honoured rather than an error. Stated here because the shape
/// of the operation is the whole of its danger: a caller that sent only the one variable it wanted
/// to change would silently delete the rest.
/// </remarks>
/// <param name="AccountId">
/// The account whose crontab is rewritten, named by row id and resolved in the handler. The
/// resolution is the tenant boundary: another tenant's id is answered "not found".
/// </param>
/// <param name="Variables">
/// The complete new set of assignments. Their VALUES are never journalled and never logged — the
/// journal records which names were set (<see cref="CronAuditJournal"/>).
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record SetCronEnvironmentCommand(
    Guid AccountId,
    IReadOnlyList<CronEnvironmentVariableDto> Variables,
    string IpAddress,
    string UserAgent);
