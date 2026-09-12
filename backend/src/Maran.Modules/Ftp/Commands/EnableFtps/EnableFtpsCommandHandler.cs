using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Domain.Policies;
using Maran.Modules.Ftp.Mappers;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Ftp.Resources;
using Maran.Modules.Ftp.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp.Commands.EnableFtps;

/// <summary>
/// Handles <see cref="EnableFtpsCommand"/>: checks the hostname is one this panel serves, asks the
/// agent to configure and start the daemon, and records what the host answered.
/// </summary>
/// <remarks>
/// <para>
/// <b>The range and the ceiling come from <see cref="FtpsDefaults"/> and can come from nowhere
/// else.</b> The command carries no numbers, so the only path from a caller to the wire is through
/// this module's own named constants — which is what keeps the panel's idea of the passive range and
/// the firewall's idea of it the same idea.
/// </para>
/// <para>
/// <b>The site check comes before the agent call, and it is not duplication.</b> Without a site for
/// the hostname there is no path by which a real certificate can ever be issued for that name, so
/// enabling would produce a daemon serving a self-signed placeholder for a name nobody can fix — and
/// the agent refuses one step later for its own reason, with a message that names a path. Refusing
/// here means the operator is told the thing they can act on.
/// </para>
/// <para>
/// <b>Why a missing certificate is <see cref="ErrorType.Failure"/> and not
/// <see cref="ErrorType.Validation"/>.</b> Nothing the operator typed is wrong. The hostname is
/// theirs, the panel serves a site for it, and the request would succeed unchanged the moment
/// certificate material exists — so a 400 telling them to check their input sends them to correct
/// something that retyping can never fix. <see cref="ErrorType.Failure"/> is documented as covering
/// "a server whose own configuration is incomplete", which is exactly this, and the resx sentence
/// names the SSL section as the place the operator goes next.
/// </para>
/// <para>
/// <b>The agent runs before the row is written.</b> A daemon running with no settings row is
/// visible on the status screen — the agent is what the screen reads — and re-enabling converges. A
/// row saying FTPS is on with no daemon behind it is a panel telling an operator their customers can
/// connect when they cannot.
/// </para>
/// </remarks>
public sealed class EnableFtpsCommandHandler
{
    /// <summary>The stable code <c>AgentErrorTranslator</c> produces for the agent's NOT_FOUND.</summary>
    /// <remarks>
    /// A private constant rather than a reference to the agent client's generated resource class,
    /// which is that project's internal detail. The string is the contract between the two
    /// projects and is asserted by this module's own tests.
    /// </remarks>
    private const string AgentNotFoundCode = "AgentNotFound";

    /// <summary>The Ftp module's database context.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>The Sdk window onto the sites this panel serves, implemented by the module that owns them.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>The agent, which owns the daemon and everything else that exists on the host.</summary>
    private readonly IAgentFtpsClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FtpAuditJournal _journal;

    /// <summary>The injectable time source; the ambient clock is a banned symbol.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    /// <param name="sites">The window onto the sites this panel serves.</param>
    /// <param name="agent">The agent client that configures and starts the daemon.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injectable time source.</param>
    public EnableFtpsCommandHandler(
        FtpDbContext dbContext,
        ISiteDirectory sites,
        IAgentFtpsClient agent,
        FtpAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _sites = sites;
        _agent = agent;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Enables FTPS for the requested hostname and answers with the daemon's own state.</summary>
    /// <param name="command">The hostname and the optional PASV address.</param>
    /// <param name="cancellationToken">Cancels the reads, the agent call and the writes.</param>
    /// <returns>
    /// What the agent observed after applying the configuration, or a typed failure:
    /// <c>FtpsHostnameNotServed</c>, <c>FtpsCertificateMissing</c>, or the agent's own.
    /// </returns>
    public async Task<Result<FtpsStatusDto>> HandleAsync(
        EnableFtpsCommand command,
        CancellationToken cancellationToken)
    {
        var site = await _sites.FindByDomainAsync(command.Hostname, cancellationToken);
        if (site is null)
        {
            return await FailAsync(
                command,
                Error.Of(nameof(ErrorMessages.FtpsHostnameNotServed), ErrorType.Validation),
                cancellationToken);
        }

        var enabled = await _agent.EnableAsync(
            command.Hostname,
            FtpsDefaults.PassivePortMin,
            FtpsDefaults.PassivePortMax,
            command.PassiveAddress,
            FtpsDefaults.MaxClients,
            cancellationToken);

        if (!enabled.IsSuccess)
        {
            return await FailAsync(command, TranslateAgentFailure(enabled.Error!), cancellationToken);
        }

        var settings = await _dbContext.FtpsSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new FtpsSettings(
                command.Hostname,
                command.PassiveAddress,
                enabled.Value.Ipv4Only,
                _clock.UtcNow);

            await _dbContext.FtpsSettings.AddAsync(settings, cancellationToken);
        }
        else
        {
            settings.Enable(
                command.Hostname,
                command.PassiveAddress,
                enabled.Value.Ipv4Only,
                _clock.UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            FtpAuditJournal.FtpsEnabled,
            command.Hostname,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<FtpsStatusDto>.Ok(FtpsStatusMapper.ToDto(enabled.Value, settings));
    }

    /// <summary>
    /// Turns the agent's NOT_FOUND into this module's own code, and passes every other failure
    /// through untouched.
    /// </summary>
    /// <param name="error">The typed failure <c>AgentErrorTranslator</c> produced.</param>
    /// <returns>The error the caller is answered with.</returns>
    /// <remarks>
    /// The agent has exactly one reason to answer NOT_FOUND on this call: no certificate material
    /// exists for the hostname. Its own message names the path it looked at, which is operator text
    /// this panel logs and never shows; the substitution here is what puts a sentence in front of an
    /// administrator that says what to do instead of where a file was not.
    /// </remarks>
    private static Error TranslateAgentFailure(Error error)
    {
        if (string.Equals(error.Code, AgentNotFoundCode, StringComparison.Ordinal))
        {
            return Error.Of(nameof(ErrorMessages.FtpsCertificateMissing), ErrorType.Failure);
        }

        return error;
    }

    /// <summary>Journals a refused enable and returns it as the typed failure.</summary>
    /// <param name="command">The enable that was refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<FtpsStatusDto>> FailAsync(
        EnableFtpsCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            FtpAuditJournal.FtpsEnabled,
            command.Hostname,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<FtpsStatusDto>.Fail(error);
    }
}
