namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>Which file-transfer daemon serves one of an account's jailed logins.</summary>
/// <remarks>
/// <para>
/// The panel-side spelling of the contract's <c>TransferProtocol</c>, mapped by NUMBER exactly as
/// <see cref="AccountLoginPasswordState"/> is, so that a value a newer agent knows and this build
/// does not reads as <see cref="Unspecified"/> rather than as whatever a cast produced.
/// </para>
/// <para>
/// <b>Why the panel needs it at all.</b> An account's logins are one list on the wire and were one
/// daemon's logins until FTPS shipped. A suspension attestation that cannot name the daemon leaves
/// the panel with a count it can only describe with the word it used to be true — "all four of its
/// SFTP logins locked" over a list that now holds FTPS ones as well. The sentence is then wrong in
/// the direction that matters: an operator reading it believes the FTP credentials were never in
/// scope, when in fact they were locked.
/// </para>
/// <para>
/// The value is DERIVED by the agent from the jail the login's passwd home sits under, never from
/// its name: <c>alice_web</c> is a valid login name under either daemon, and the jail root is a
/// value the agent itself wrote.
/// </para>
/// </remarks>
public enum LoginTransferProtocol
{
    /// <summary>
    /// The agent did not say, because it predates the field. A caller MUST read such a login as
    /// <see cref="Sftp"/> — before FTPS existed, every login a suspension could report was an SFTP
    /// login by construction, so that is the correct reading rather than a guess. This is the one
    /// absent value in this contract that resolves to a concrete answer instead of to a refusal.
    /// </summary>
    Unspecified = 0,

    /// <summary>Served by the host's OpenSSH daemon, jailed under the panel's SFTP jail root.</summary>
    Sftp = 1,

    /// <summary>Served by this panel's own vsftpd instance, jailed under the panel's FTPS jail root.</summary>
    Ftps = 2,
}
