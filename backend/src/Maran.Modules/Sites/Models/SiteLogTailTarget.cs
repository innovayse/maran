using Maran.Agent.Client.Services.SitesService;

namespace Maran.Modules.Sites.Models;

/// <summary>
/// What a tail request resolved to once the caller's right to it was established: the system user
/// and domain that address the agent, and the validated stream parameters.
/// </summary>
/// <remarks>
/// It exists so that authorization happens BEFORE a single byte of the response is written. Once
/// the response has begun as <c>text/event-stream</c>, a refusal can no longer be a 404 — it would
/// have to be an error inside a stream the caller was already told they may read. Resolving first
/// and streaming second keeps the refusal an ordinary HTTP status.
/// </remarks>
/// <param name="AccountUsername">System user name of the owning account, which addresses the agent.</param>
/// <param name="Domain">The site's primary domain, which names the log files.</param>
/// <param name="Source">Which of the site's two logs to read.</param>
/// <param name="HistoryLines">How many existing lines to replay before live ones; the agent caps this too.</param>
public sealed record SiteLogTailTarget(
    string AccountUsername,
    string Domain,
    SiteLogSource Source,
    uint HistoryLines);
