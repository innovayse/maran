namespace Maran.Modules.Sftp.Tests.TestSupport;

/// <summary>One account-wide SFTP lock change the handler under test asked the agent for.</summary>
/// <remarks>
/// It carries no login name, and that absence is the thing worth recording: the agent enumerates the
/// account's logins from the host's own password database rather than from this module's rows,
/// because a row the panel has forgotten is exactly the login that would keep letting a suspended
/// customer in.
/// </remarks>
/// <param name="AccountUsername">System username of the account whose every login was addressed.</param>
/// <param name="Locked">Whether the call locked the account's logins or unlocked them.</param>
public sealed record AgentAccountLockCall(string AccountUsername, bool Locked);
