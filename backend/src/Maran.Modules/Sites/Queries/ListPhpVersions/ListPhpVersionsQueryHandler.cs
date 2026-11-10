using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Services;

namespace Maran.Modules.Sites.Queries.ListPhpVersions;

/// <summary>Handles <see cref="ListPhpVersionsQuery"/> by asking the agent what the host has installed.</summary>
/// <remarks>
/// Host-level reference data, not tenant data: multi-PHP is installed once per server and then
/// bound to any number of sites, so there is nothing here to scope to an account. The agent's
/// answer is reshaped by <see cref="PhpVersionListing"/> into the module's own <see cref="PhpVersionDto"/>
/// (through <c>PhpVersionMapper</c>), which drops the FPM socket directory — a filesystem path
/// has no business on a customer's screen (rules/security.md) — and adds where each version stands in
/// its support life and, for an administrator, how many sites use it.
/// </remarks>
public sealed class ListPhpVersionsQueryHandler
{
    /// <summary>The agent client listing the host's installed PHP runtimes.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>Describes the agent's versions in the panel's outward shape.</summary>
    private readonly PhpVersionListing _listing;

    /// <summary>Reads which PHP versions are installed as apps.</summary>
    private readonly SiteStackGate _stack;

    /// <summary>Creates the handler.</summary>
    /// <param name="php">The agent client listing the host's installed PHP runtimes.</param>
    /// <param name="listing">Describes the agent's versions in the panel's outward shape.</param>
    /// <param name="stack">Reads which PHP versions are installed as apps.</param>
    public ListPhpVersionsQueryHandler(
        IAgentPhpClient php,
        PhpVersionListing listing,
        SiteStackGate stack)
    {
        _stack = stack;
        _php = php;
        _listing = listing;
    }

    /// <summary>Returns the installed versions, or the agent's own typed failure.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The installed versions, or the agent's typed error.</returns>
    public async Task<Result<IReadOnlyList<PhpVersionDto>>> HandleAsync(
        ListPhpVersionsQuery query,
        CancellationToken cancellationToken)
    {
        // PHP is an app like any other: with none installed there is no PHP to report on, and the answer
        // says so rather than listing an empty or stale host. Enforced here, not in the SPA, which only
        // hides the menu (docs/apps/README.md).
        var installed = await _stack.ReadAsync(cancellationToken);
        if (installed.PhpVersions.Count == 0)
        {
            return Result<IReadOnlyList<PhpVersionDto>>.Fail(SiteHostApps.NotInstalledFailure());
        }

        var versions = await _php.ListVersionsAsync(cancellationToken);
        if (!versions.IsSuccess)
        {
            return Result<IReadOnlyList<PhpVersionDto>>.Fail(versions.Error!);
        }

        var described = await _listing.DescribeAsync(versions.Value, cancellationToken);

        return Result<IReadOnlyList<PhpVersionDto>>.Ok(described);
    }
}
