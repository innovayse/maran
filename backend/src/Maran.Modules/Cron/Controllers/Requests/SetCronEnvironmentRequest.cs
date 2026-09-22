using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Controllers.Requests;

/// <summary>The body of <c>PUT /api/v1/cron-environment</c>.</summary>
/// <remarks>
/// <c>PUT</c> and not <c>PATCH</c>, because the operation really is a replacement: what this body
/// carries is exactly what the managed region of the crontab will hold afterwards, and a name absent
/// from it is removed. The verb is the warning.
/// </remarks>
/// <param name="AccountId">The account whose crontab is rewritten.</param>
/// <param name="Variables">The complete new set of assignments; an empty list clears them all.</param>
public sealed record SetCronEnvironmentRequest(
    Guid AccountId,
    IReadOnlyList<CronEnvironmentVariableDto> Variables);
