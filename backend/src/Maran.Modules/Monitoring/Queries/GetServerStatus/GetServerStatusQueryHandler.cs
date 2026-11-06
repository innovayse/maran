using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetPendingUpdateCount;
using Maran.Modules.Monitoring.Queries.GetRebootRequirement;
using Wolverine;

namespace Maran.Modules.Monitoring.Queries.GetServerStatus;

/// <summary>
/// Handles <see cref="GetServerStatusQuery"/> by composing the two existing agent reads through the
/// bus, so the figures an integration sees are the very ones the administrator's screens show (issue #144).
/// </summary>
/// <remarks>
/// Nothing is invented. Each member is exactly what its own query measured: a <c>null</c> stays
/// <c>null</c> ("not measured") and is never turned into zero or <c>false</c>. When either read fails
/// the whole answer is that read's own typed failure, never a partial document whose missing half
/// could be taken for a healthy host. The first read runs before the second, so a failing agent is
/// asked once, not twice.
/// </remarks>
public sealed class GetServerStatusQueryHandler
{
    /// <summary>The bus the two existing queries are invoked through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the handler.</summary>
    /// <param name="bus">The bus the two existing queries are invoked through.</param>
    public GetServerStatusQueryHandler(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <summary>Returns the pending update counts and the reboot requirement.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The composed status, or the typed failure of whichever read failed first.</returns>
    public async Task<Result<ServerStatusDto>> HandleAsync(
        GetServerStatusQuery query,
        CancellationToken cancellationToken)
    {
        var updates = await _bus.InvokeAsync<Result<PendingUpdateCountDto>>(
            new GetPendingUpdateCountQuery(),
            cancellationToken);

        if (!updates.IsSuccess)
        {
            return Result<ServerStatusDto>.Fail(updates.Error!);
        }

        var reboot = await _bus.InvokeAsync<Result<RebootRequirementDto>>(
            new GetRebootRequirementQuery(),
            cancellationToken);

        if (!reboot.IsSuccess)
        {
            return Result<ServerStatusDto>.Fail(reboot.Error!);
        }

        return Result<ServerStatusDto>.Ok(
            new ServerStatusDto(updates.Value.Count, updates.Value.SecurityCount, reboot.Value.Required));
    }
}
