using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Mappers;
using Maran.Modules.Ftp.Persistence;

namespace Maran.Modules.Ftp.Queries.GetFtpsStatus;

/// <summary>
/// Handles <see cref="GetFtpsStatusQuery"/> by asking the agent what the daemon is doing and adding
/// the two facts only the panel holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every daemon fact is measured, not remembered.</b> The settings row is read for the hostname
/// and for the panel's intention, and for nothing else: it supplies no running flag, no port range
/// and no TLS answer. A status assembled from it would agree with the panel by construction, and the
/// disagreement is the whole content of the screen — a crashed unit under a row saying "enabled",
/// and mandatory TLS switched off on a daemon that is otherwise healthy.
/// </para>
/// <para>
/// <b>A server with no settings row still gets an answer.</b> The hostname passed to the agent is
/// then EMPTY, which the contract defines as "report the daemon and range facts only": the three
/// certificate fields come back absent-valued because nothing was asked about, not because nothing
/// exists. That is why the answer carries the hostname as <c>null</c> beside them — a screen reads
/// the pair, and a caller that did not name a hostname must not read "no certificate" out of a call
/// it never made.
/// </para>
/// </remarks>
public sealed class GetFtpsStatusQueryHandler
{
    /// <summary>The Ftp module's database context.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>The agent, which is the only thing that can observe the daemon.</summary>
    private readonly IAgentFtpsClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    /// <param name="agent">The agent client that observes the daemon.</param>
    public GetFtpsStatusQueryHandler(FtpDbContext dbContext, IAgentFtpsClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
    }

    /// <summary>Returns what the daemon is doing, or the agent's own typed failure.</summary>
    /// <param name="query">Carries nothing; the hostname comes from the panel's own row.</param>
    /// <param name="cancellationToken">Cancels the read and the agent call.</param>
    /// <returns>The status an administrator's screen renders, or a typed failure.</returns>
    public async Task<Result<FtpsStatusDto>> HandleAsync(
        GetFtpsStatusQuery query,
        CancellationToken cancellationToken)
    {
        var settings = await _dbContext.FtpsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        var observed = await _agent.GetStatusAsync(settings?.Hostname ?? string.Empty, cancellationToken);
        if (!observed.IsSuccess)
        {
            return Result<FtpsStatusDto>.Fail(observed.Error!);
        }

        return Result<FtpsStatusDto>.Ok(FtpsStatusMapper.ToDto(observed.Value, settings));
    }
}
