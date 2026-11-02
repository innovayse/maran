using Maran.Agent.Client.Services.AccountsService;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// How the account lifecycle reads and words the transfer logins the host reports for an account.
/// </summary>
/// <remarks>
/// <para>
/// One list on the wire carries the logins of BOTH file-transfer daemons since FTPS shipped, told
/// apart by <see cref="FileTransferLoginSuspensionFactDto.Protocol"/> and by nothing else — a login's NAME
/// says nothing, because <c>alice_web</c> is a valid login under either daemon. Every sentence this
/// module tells an operator about that list therefore has to ask this type what it is looking at,
/// and there are two handlers telling those sentences: the suspension's and the resumption's. The
/// rule lives here once so the two cannot drift into wording the same list differently.
/// </para>
/// <para>
/// <b>This is wording and not a reading of the wire.</b> A protocol an old agent did not state is
/// resolved to <see cref="LoginTransferProtocol.Sftp"/> by the agent client, once, at the boundary —
/// before FTPS existed every fact in this list was an SFTP login by construction — so everything
/// here works from a daemon the host named.
/// </para>
/// </remarks>
public static class TransferLoginProtocolPolicy
{
    /// <summary>Names one login the way an operator sent to look at it needs to read it.</summary>
    /// <param name="login">The fact the host reported for one of the account's jailed logins.</param>
    /// <returns>The login's system name, suffixed with the daemon that serves it.</returns>
    /// <remarks>
    /// The suffix is always written: the client resolves a protocol an old agent did not state to
    /// SFTP before it reaches this module, so every login here has a daemon the panel can name.
    /// </remarks>
    public static string Describe(FileTransferLoginSuspensionFactDto login)
    {
        return login.Protocol switch
        {
            LoginTransferProtocol.Sftp => $"{login.Username} (sftp)",
            LoginTransferProtocol.Ftps => $"{login.Username} (ftps)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(login),
                login.Protocol,
                "Unmapped transfer protocol."),
        };
    }

    /// <summary>Words the whole list as counts per daemon, with no verb: the caller supplies that.</summary>
    /// <param name="logins">Every jailed login the host reported for the account.</param>
    /// <returns>
    /// A noun phrase such as <c>all 3 of its sftp logins and all 2 of its ftps logins</c>, which a
    /// caller completes with <c>locked</c> or <c>unlocked</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The verb is the caller's because the two attestations state opposite facts about the same
    /// list, and a shared renderer that took the word as a parameter would be one sentence pretending
    /// to be two.
    /// </para>
    /// <para>
    /// <b>BOTH daemons are always named, zero included, and that is a correction.</b> This method
    /// used to omit a daemon with no logins, so that a host without FTPS read exactly as it read
    /// before FTPS shipped. Read on a live panel, the rule came out asymmetric: SFTP was named
    /// whatever its count, FTPS only when non-zero, so an account with no logins of either kind
    /// printed <c>all 0 of its sftp logins</c> and said nothing about the other daemon. That is the
    /// defect this type exists to end, arriving from the other side — an operator reads one daemon
    /// named and one absent as "SFTP was in scope and FTPS was not", over a suspension that locks
    /// both unconditionally, and the commonest account on any host is the one that reads that way.
    /// </para>
    /// <para>
    /// The reason the omission existed does not survive the comparison. It protected the SHAPE OF A
    /// SENTENCE across a release, which no operator is reading for, and it bought that with a rule
    /// where the same number — zero — means "in scope, nothing to do" for one daemon and "not
    /// mentioned" for the other. The sentence this phrase lands in already says <c>all 0 of its
    /// vhosts</c> and <c>all 0 of its cron entries</c>, so <c>all 0</c> is that sentence's own idiom
    /// for an empty count and the special case was the anomaly, not the symmetry. One unconditional
    /// phrase also has no branch that can go unexercised.
    /// </para>
    /// </remarks>
    public static string DescribeAll(IReadOnlyList<FileTransferLoginSuspensionFactDto> logins)
    {
        var ftps = logins.Count(login => { return login.Protocol == LoginTransferProtocol.Ftps; });
        var sftp = logins.Count - ftps;

        return $"all {sftp} of its sftp logins and all {ftps} of its ftps logins";
    }
}
