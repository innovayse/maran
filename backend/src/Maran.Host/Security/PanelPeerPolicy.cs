namespace Maran.Host.Security;

/// <summary>
/// Decides which unix uid may use the panel's listening socket at all.
/// </summary>
/// <remarks>
/// The panel is reached only through nginx, so authorisation starts below HTTP: a caller that is
/// not the web server never reaches a middleware, a controller, or the forwarded-header
/// machinery, whatever it asks for. This is the rule the agent already applies to the panel from
/// the other side (<c>agent/crates/agent/src/peercred/peer_policy.rs</c>), applied here to the
/// panel's own callers.
///
/// <para>
/// One uid, with no special case for root: an allow-list of exactly one is the only rule that
/// cannot be widened by accident. An unconfigured policy permits nobody, because the alternative
/// — treating "no uid configured" as "any uid" — is the fail-open shape this whole area exists to
/// keep out.
/// </para>
/// </remarks>
public readonly record struct PanelPeerPolicy
{
    /// <summary>The single uid allowed to connect, or <see langword="null"/> when none is.</summary>
    private readonly uint? _allowedUid;

    /// <summary>Creates a policy allowing exactly <paramref name="allowedUid"/>.</summary>
    /// <param name="allowedUid">The uid to permit, or <see langword="null"/> to permit nobody.</param>
    public PanelPeerPolicy(uint? allowedUid)
    {
        _allowedUid = allowedUid;
    }

    /// <summary>Whether the policy names a uid at all.</summary>
    /// <remarks>
    /// Read at startup to tell an unconfigured panel apart from a misconfigured one: the first
    /// deserves a message naming the missing setting, the second is simply a refusal.
    /// </remarks>
    public bool IsConfigured
    {
        get
        {
            return _allowedUid is not null;
        }
    }

    /// <summary>Whether <paramref name="peerUid"/> may use the panel's socket.</summary>
    /// <param name="peerUid">The uid the kernel reported for the connecting process.</param>
    /// <returns>True only when a uid is configured and it is exactly this one.</returns>
    public bool Permits(uint peerUid)
    {
        return _allowedUid is not null && peerUid == _allowedUid.Value;
    }
}
