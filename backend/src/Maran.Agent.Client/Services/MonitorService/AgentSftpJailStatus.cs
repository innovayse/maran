namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>
/// What the agent found when it checked the live <c>sshd_config</c> for the installer's own
/// <c>Match Group</c> block — the block that turns membership of the SFTP group into a jailed,
/// shell-less, port-forward-less login.
/// </summary>
/// <param name="IsDrifted">
/// <c>false</c> when the block is present and every directive it is supposed to carry is still
/// there with its original value; <c>true</c> when the block is missing, or present but missing one
/// or more of its directives — including the exact failure the panel has no other way to see: a
/// package upgrade or a hand edit that removed the block, after which every SFTP login on the host
/// is a full shell session and nothing in the panel would have noticed without this check.
/// </param>
/// <param name="Missing">
/// What the installer's block promises that this reading did not find, in the agent's own words —
/// empty when <paramref name="IsDrifted"/> is <c>false</c>. Never a tool's standard error: the agent
/// names the missing marker text or directive, not a program's output.
/// </param>
/// <remarks>
/// This is a claim about the FILE the agent read, not about what <c>sshd</c> enforces for a real
/// connection — see the agent's own doc comment on <c>MonitorHost::read_sshd_config</c> for exactly
/// what a file read can and cannot prove, and why asking <c>sshd</c> for its effective configuration
/// was tried first and rejected.
/// </remarks>
public sealed record AgentSftpJailStatus(bool IsDrifted, IReadOnlyList<string> Missing);
