namespace Maran.Modules.Firewall.Domain.ValueObjects;

/// <summary>
/// The answer <see cref="Policies.FirewallLockoutPolicy"/> gives about one allow: harmless, or the
/// SSH port the caller would stop reaching.
/// </summary>
/// <param name="LostSshPort">
/// The SSH port the caller would no longer reach, or <c>null</c> when the allow is harmless. One
/// field with one meaning: there is no separate flag to disagree with it, and no zero standing in
/// for "none".
/// </param>
public sealed record FirewallLockoutVerdict(int? LostSshPort)
{
    /// <summary>The verdict for an allow that cannot cost the caller an SSH port.</summary>
    public static readonly FirewallLockoutVerdict Harmless = new((int?)null);

    /// <summary>Whether the allow would stop the caller reaching an SSH port.</summary>
    public bool LocksOutCaller
    {
        get
        {
            return LostSshPort is not null;
        }
    }

    /// <summary>Builds the verdict for an allow that would cost the caller an SSH port.</summary>
    /// <param name="sshPort">The SSH port the caller would stop reaching.</param>
    /// <returns>A verdict whose <see cref="LocksOutCaller"/> is true.</returns>
    public static FirewallLockoutVerdict Locks(int sshPort)
    {
        return new FirewallLockoutVerdict(sshPort);
    }
}
