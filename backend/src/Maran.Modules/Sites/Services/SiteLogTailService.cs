using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Turns "this caller wants to watch that site's log" into a sequence of frames a client can be
/// shown. Two steps, deliberately separate: <see cref="ResolveAsync"/> settles whether the caller
/// may read the log at all, and <see cref="ReadAsync"/> streams it.
/// </summary>
/// <remarks>
/// The split is what keeps the refusal an ordinary status code. An HTTP response can only carry one
/// status, and it is fixed the moment the first byte is written; a stream that authorized as it went
/// would have to report "not yours" inside a body already announced as a successful event stream.
/// So the site is resolved through the tenant-filtered <see cref="SitesDbContext"/> first — another
/// customer's site is simply not there, which is a 404 and never a 403 (rules/testing.md item 3).
///
/// The reading half's one invariant: the sequence it returns ALWAYS ends with exactly one frame
/// naming an ending. The agent client already promises the same of its own events; this repeats the
/// promise rather than trusting it, because a broken promise upstream would surface here as a log
/// pane that stopped updating with nothing said — the failure mode the whole typed-ending chain was
/// built to remove.
/// </remarks>
public sealed class SiteLogTailService
{
    /// <summary>The largest history replay a caller may ask for; beyond it the request is refused.</summary>
    /// <remarks>
    /// Refused rather than silently clamped: a caller who asked for a million lines and was given
    /// a thousand would read a truncated log believing it complete, which is the same lie as an
    /// unnamed ending.
    ///
    /// The number is the AGENT'S ceiling, not one of the panel's choosing: <c>sites.proto</c>'s
    /// TailSiteLogRequest says "capped by the agent at 1000 regardless of the requested value", and
    /// the agent clamps there silently. A larger number here would accept a request the panel
    /// cannot honour and hand back a short log with nothing said — the clamp this refusal exists to
    /// avoid, moved one layer out and made invisible. If the contract's cap changes, this changes
    /// with it.
    /// </remarks>
    private const int MaxHistoryLines = 1_000;

    /// <summary>The wire name of the access log.</summary>
    private const string AccessSourceName = "access";

    /// <summary>The wire name of the error log.</summary>
    private const string ErrorSourceName = "error";

    /// <summary>The Sites module's tenant-filtered database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The owning account's system user name, which addresses every agent operation.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which is the only thing that may read a customer's files.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>Resolves an error code to the sentence the caller is shown.</summary>
    private readonly IErrorTextProvider _errorText;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The Sites module's tenant-filtered database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that reads the log.</param>
    /// <param name="errorText">Resolves an error code to the sentence the caller is shown.</param>
    /// <param name="journal">This module's audit journal.</param>
    public SiteLogTailService(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSitesClient agent,
        IErrorTextProvider errorText,
        SiteAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _errorText = errorText;
        _journal = journal;
    }

    /// <summary>Settles whether this caller may tail this log, and with which parameters.</summary>
    /// <param name="siteId">The site whose log was asked for.</param>
    /// <param name="source">The requested log, as the caller spelled it.</param>
    /// <param name="historyLines">How many existing lines the caller asked to replay.</param>
    /// <param name="ipAddress">The caller's address, for the journal.</param>
    /// <param name="userAgent">The caller's user agent, for the journal.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>
    /// What to tail, or <c>SiteLogSourceInvalid</c>, <c>SiteLogHistoryLinesInvalid</c>,
    /// <c>SiteNotFound</c> or <c>AccountNotFound</c>. A site belonging to another customer is
    /// <c>SiteNotFound</c> — the query filter means the row genuinely is not in the result set.
    /// </returns>
    public async Task<Result<SiteLogTailTarget>> ResolveAsync(
        Guid siteId,
        string source,
        int historyLines,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        // The site is resolved BEFORE the query parameters are looked at, and the order is load
        // bearing. Validating first would answer 400 for a malformed request against a site the
        // caller may not see, and 404 for a well-formed one — which tells an attacker with a bad
        // "source" value that the site exists, the very thing the 404-never-403 rule removes.
        var site = await _dbContext.Sites
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == siteId, cancellationToken);
        if (site is null)
        {
            // The subject is the identifier the caller supplied, because no domain is known: a probe
            // for a log the caller may not read still leaves a trace naming what was probed for.
            return await FailAsync(siteId.ToString(), nameof(ErrorMessages.SiteNotFound), ipAddress, userAgent, cancellationToken);
        }

        var account = await _accounts.FindAsync(site.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(site.Domain, nameof(ErrorMessages.AccountNotFound), ipAddress, userAgent, cancellationToken);
        }

        var logSource = ParseSource(source);
        if (logSource is null)
        {
            return await FailAsync(
                site.Domain, nameof(ErrorMessages.SiteLogSourceInvalid), ipAddress, userAgent, cancellationToken);
        }

        if (historyLines < 0 || historyLines > MaxHistoryLines)
        {
            return await FailAsync(
                site.Domain, nameof(ErrorMessages.SiteLogHistoryLinesInvalid), ipAddress, userAgent, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.SiteLogTailed, site.Domain, ipAddress, userAgent, cancellationToken);

        return Result<SiteLogTailTarget>.Ok(new SiteLogTailTarget(
            account.Username, site.Domain, logSource.Value, (uint)historyLines));
    }

    /// <summary>Streams the log as frames, ending with exactly one frame that names the ending.</summary>
    /// <param name="target">What <see cref="ResolveAsync"/> settled.</param>
    /// <param name="cancellationToken">
    /// Cancelled when the watching client goes away. It stops the agent's stream too, so an
    /// abandoned tail leaves neither a connection nor a background reader behind.
    /// </param>
    /// <returns>The line frames, then the terminal frame.</returns>
    public async IAsyncEnumerable<SiteLogFrame> ReadAsync(
        SiteLogTailTarget target,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var events = _agent.TailLogAsync(
            target.AccountUsername, target.Domain, target.Source, target.HistoryLines, cancellationToken);

        await using var enumerator = events.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            SiteLogEvent? next;
            var cancelled = false;
            try
            {
                next = await enumerator.MoveNextAsync() ? enumerator.Current : null;
            }
            catch (OperationCanceledException)
            {
                // The caller stopped watching mid-read. Caught and named rather than allowed to
                // surface as a broken enumeration, so the last thing produced is still an ending.
                // (The yield cannot live in the catch clause itself; C# forbids it.)
                next = null;
                cancelled = true;
            }

            if (cancelled)
            {
                yield return SiteLogFrame.OfEnd(SiteLogEndReason.Cancelled, null);
                yield break;
            }

            if (next is null)
            {
                // The agent client promises a terminal event and did not deliver one. Reported as
                // truncated — not completed — because lines may be missing and we cannot know.
                yield return SiteLogFrame.OfEnd(SiteLogEndReason.Truncated, null);
                yield break;
            }

            if (next.Kind == SiteLogEventKind.Line)
            {
                yield return SiteLogFrame.OfLine(next.Line, next.Historical);
                continue;
            }

            yield return ToTerminalFrame(next);
            yield break;
        }
    }

    /// <summary>Translates the agent client's terminal event into the frame the client is shown.</summary>
    /// <param name="terminal">The agent client's terminal event.</param>
    /// <returns>The terminal frame, carrying a localized sentence only where one adds anything.</returns>
    private SiteLogFrame ToTerminalFrame(SiteLogEvent terminal)
    {
        if (terminal.Kind == SiteLogEventKind.Failed)
        {
            // The code, resolved to a sentence here. The agent's own text never travels: it can
            // name absolute paths on the host (rules/security.md).
            var code = terminal.ErrorCode ?? nameof(ErrorMessages.SiteLogTailFailed);
            return SiteLogFrame.OfEnd(SiteLogEndReason.Failed, _errorText.Resolve(code));
        }

        return SiteLogFrame.OfEnd(ToReason(terminal.Kind), null);
    }

    /// <summary>Maps a non-failing terminal event kind onto its wire reason.</summary>
    /// <param name="kind">The agent client's terminal kind.</param>
    /// <returns>The reason the client is told.</returns>
    private static SiteLogEndReason ToReason(SiteLogEventKind kind)
    {
        return kind switch
        {
            SiteLogEventKind.Completed => SiteLogEndReason.Completed,
            SiteLogEventKind.Dropped => SiteLogEndReason.Dropped,
            SiteLogEventKind.Idle => SiteLogEndReason.Idle,
            SiteLogEventKind.Cancelled => SiteLogEndReason.Cancelled,

            // A kind this map does not know is not an ending we can describe, and the safe answer
            // is the pessimistic one: something may be missing.
            _ => SiteLogEndReason.Truncated,
        };
    }

    /// <summary>Reads the caller's spelling of the log source.</summary>
    /// <param name="source">The <c>source</c> query value.</param>
    /// <returns>The log to read, or <c>null</c> when the value names neither log.</returns>
    private static SiteLogSource? ParseSource(string source)
    {
        return source switch
        {
            AccessSourceName => SiteLogSource.Access,
            ErrorSourceName => SiteLogSource.Error,
            _ => null,
        };
    }

    /// <summary>Journals a refused tail and returns it as the typed failure.</summary>
    /// <param name="subject">The site's domain, or the identifier the caller supplied.</param>
    /// <param name="code">The machine-stable code to answer with.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="code"/>.</returns>
    private async Task<Result<SiteLogTailTarget>> FailAsync(
        string subject,
        string code,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.SiteLogTailed, subject, ipAddress, userAgent, cancellationToken);

        return Result<SiteLogTailTarget>.Fail(Error.Of(code));
    }
}
