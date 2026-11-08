using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Rewrites an account's php-fpm pools with a set of settings, one pool per PHP version, and puts the
/// ones already written back when a later one fails (issue #138).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why compensation and not a transaction.</b> The pools are files on the host, one per version,
/// each written by its own rpc. The agent makes each rpc atomic for its own file (it restores the
/// previous pool when the new one fails <c>php-fpm -t</c> or its reload); nothing spans two of them. So
/// the applier does what <see cref="SiteVhostCompensator"/> does for a vhost: when the second version
/// fails it sends the first the settings it had before, so an account is never left with one version's
/// pool on the new settings and another's on the old.
/// </para>
/// <para>
/// <b>Compensation is best effort and says so.</b> A restore that fails too is not retried here: the
/// panel's start-up pass converges every pool to the stored settings, and the failure the caller
/// answers with is the first one. A caller that has no previous set to restore (a plan change, where
/// the previous pools are unknown) passes none, and nothing is restored.
/// </para>
/// </remarks>
public sealed class PhpPoolApplier
{
    /// <summary>The agent's PHP runtimes and pools.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>Creates the applier.</summary>
    /// <param name="php">The agent client that writes pools.</param>
    public PhpPoolApplier(IAgentPhpClient php)
    {
        _php = php;
    }

    /// <summary>Rewrites the account's pool at each version with <paramref name="desired"/>.</summary>
    /// <param name="username">The account's Linux system user name.</param>
    /// <param name="maxChildren">The plan's worker budget for one pool.</param>
    /// <param name="versions">The versions the account has a pool at; none means nothing to write.</param>
    /// <param name="desired">The settings the pools are to carry, already cut to the plan.</param>
    /// <param name="previous">
    /// The settings the pools carried before, sent back to the versions already written when a later one
    /// fails; null when the caller cannot name them.
    /// </param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>Success, or the agent's own typed failure for the first version that refused.</returns>
    public async Task<Result> ApplyAsync(
        string username,
        int maxChildren,
        IReadOnlyList<string> versions,
        IReadOnlyList<PhpSettingDto> desired,
        IReadOnlyList<PhpSettingDto>? previous,
        CancellationToken cancellationToken)
    {
        var written = new List<string>();
        foreach (var version in versions)
        {
            var applied = await _php.ApplyPoolSettingsAsync(
                username, version, (uint)maxChildren, desired, cancellationToken);
            if (!applied.IsSuccess)
            {
                if (previous is not null)
                {
                    await RestoreAsync(username, maxChildren, written, previous);
                }

                return applied;
            }

            written.Add(version);
        }

        return Result.Ok();
    }

    /// <summary>Sends the previous settings back to the versions already rewritten.</summary>
    /// <param name="username">The account's Linux system user name.</param>
    /// <param name="maxChildren">The plan's worker budget for one pool.</param>
    /// <param name="written">The versions already rewritten.</param>
    /// <param name="previous">The settings those pools carried before.</param>
    private async Task RestoreAsync(
        string username,
        int maxChildren,
        IReadOnlyList<string> written,
        IReadOnlyList<PhpSettingDto> previous)
    {
        foreach (var version in written)
        {
            // Not tied to the request's token: the request that failed may be the one that was cancelled,
            // and an account left half-written because of that is the state this type exists to prevent.
            await _php.ApplyPoolSettingsAsync(username, version, (uint)maxChildren, previous, CancellationToken.None);
        }
    }
}
