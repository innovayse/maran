namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>
/// What the agent found when it checked the live <c>sshd_config</c> for the installer's own
/// <c>Match Group</c> block — the block that turns membership of the SFTP group into a jailed,
/// shell-less, port-forward-less login. Exactly one of two cases, each its own type, so the list of
/// what is missing exists only on the case that has one.
/// </summary>
/// <remarks>
/// <para>
/// This is a claim about the FILE the agent read, not about what <c>sshd</c> enforces for a real
/// connection — see the agent's own doc comment on <c>MonitorHost::read_sshd_config</c> for exactly
/// what a file read can and cannot prove, and why asking <c>sshd</c> for its effective configuration
/// was tried first and rejected.
/// </para>
/// <para>
/// <b>There is no case for "the agent did not say".</b> A reply whose state the panel cannot name is
/// answered by the client with <c>AgentInvalidResponse</c>, never with a drifted block that lists
/// nothing missing. The constructor is private to this assembly, so the two nested cases are the
/// whole set.
/// </para>
/// </remarks>
public abstract record AgentSftpJailStatus
{
    /// <summary>Closes the hierarchy to the two cases declared below.</summary>
    private AgentSftpJailStatus()
    {
    }

    /// <summary>
    /// The block is present and every directive it is supposed to carry is still there with its
    /// original value.
    /// </summary>
    public sealed record Intact : AgentSftpJailStatus;

    /// <summary>
    /// The block is missing, or present but missing one or more of its directives — including the
    /// exact failure the panel has no other way to see: a package upgrade or a hand edit that removed
    /// the block, after which every SFTP login on the host is a full shell session and nothing in the
    /// panel would have noticed without this check.
    /// </summary>
    /// <param name="Missing">
    /// What the installer's block promises that this reading did not find, in the agent's own words.
    /// Never a tool's standard error: the agent names the missing marker text or directive, not a
    /// program's output. Empty when the whole block is missing and the agent names no directive.
    /// </param>
    public sealed record Drifted(IReadOnlyList<string> Missing) : AgentSftpJailStatus;
}
