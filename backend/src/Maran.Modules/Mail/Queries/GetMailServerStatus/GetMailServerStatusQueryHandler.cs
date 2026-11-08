using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;

namespace Maran.Modules.Mail.Queries.GetMailServerStatus;

/// <summary>
/// Handles <see cref="GetMailServerStatusQuery"/>: reads the panel's intention from the settings row
/// and the host's state from the agent, and reports both side by side.
/// </summary>
/// <remarks>
/// The two are never merged. The row says what an administrator asked for; the agent says what the
/// daemons do, read from the host on every call, so the screen cannot agree with the panel by
/// construction. A failed agent read fails the query: a status built from the row alone would
/// present the intention as an observation.
/// </remarks>
public sealed class GetMailServerStatusQueryHandler
{
    /// <summary>The Mail module's database context.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The agent, the only source of what the host does.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="agent">The agent client that reads the host.</param>
    public GetMailServerStatusQueryHandler(MailDbContext dbContext, IAgentMailClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
    }

    /// <summary>Builds the status.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellationToken">Cancels the read and the agent call.</param>
    /// <returns>The status, or the agent's typed failure.</returns>
    public async Task<Result<MailServerStatusDto>> HandleAsync(
        GetMailServerStatusQuery query,
        CancellationToken cancellationToken)
    {
        var settings = await _dbContext.MailServerSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        var observed = await _agent.GetStatusAsync(cancellationToken);
        if (!observed.IsSuccess)
        {
            return Result<MailServerStatusDto>.Fail(observed.Error!);
        }

        return Result<MailServerStatusDto>.Ok(MailServerStatusMapper.ToDto(observed.Value, settings));
    }
}
