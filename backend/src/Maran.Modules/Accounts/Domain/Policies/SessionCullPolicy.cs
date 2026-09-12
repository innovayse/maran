using Maran.Sdk.Events;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// What a suspension attestation may say about the transfer sessions the suspension ended.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this clause has to be on the line at all.</b> A suspension does not only refuse the
/// account's next login — it signals every process running as the account's uid, so a transfer in
/// flight is cut at whatever byte it had reached and the partial file stays in the customer's home.
/// The panel tells the operator that before they confirm. An attestation that left the action out
/// would be the operator's record of a suspension that did one privileged thing more than the record
/// said, which is the defect this whole attestation exists to end.
/// </para>
/// <para>
/// <b>Four readings over three facts, and the reason none may be collapsed.</b> A number above zero
/// is a report of work done. Zero is a completeness claim of the strongest kind available here — the
/// agent ran <c>pkill</c> against the account's uid and it matched no process, which is the state a
/// suspension is trying to reach. Absence is neither, and it arrives two ways that an operator can
/// act on differently: a module reported the cull but the HOST's answer carried no count (an agent
/// predating the wire field), or no module reported at all (nothing that ends sessions was composed,
/// or nothing ran). Both absences say the panel does not know, so they add no fourth fact; they are
/// worded apart because only one of them means the cull may never have been attempted.
/// </para>
/// <para>
/// <b>How absence is recognised, and why it is not an inference here.</b> Two presence signals, both
/// structural. <see cref="AccountSuspensionCascadeReport.SessionCullReported"/> is written by the
/// subscriber that performs the cull and by nothing else, so an unanswered cascade cannot look like an
/// answered one. <see cref="AccountSuspensionCascadeReport.SessionsEnded"/> is <c>null</c> exactly
/// when the wire's <c>optional uint32 sessions_ended</c> was absent, so an agent that counted nothing
/// cannot look like one that predates the field. Contrast <see cref="UnmanagedLoginPolicy"/>, which
/// has to INFER that its count was measured from an unrelated field of the same answer because that
/// field is a bare <c>uint32</c> — and which therefore reads the commonest account as unknown for
/// ever. The wire field this policy reads was made <c>optional</c> so that the same price would not be
/// paid twice.
/// </para>
/// <para>
/// <b>A failed cull is not one of these readings.</b> The agent answers an error rather than a count
/// when some of the account's processes may have been signalled and some may not, the Sftp subscriber
/// throws on that error, and the suspension is abandoned with the account left active — so no
/// attestation is rendered at all and the task carries a failure instead. That is the distinguishable
/// reading of the third thing the agent's cull can answer, and it is deliberately not a sentence on a
/// completed line.
/// </para>
/// <para>
/// Every wording says what was done or refuses to say, and none of them claims an observation the
/// panel made itself: the panel does not watch the processes die, it carries the agent's own count of
/// what it signalled.
/// </para>
/// </remarks>
public static class SessionCullPolicy
{
    /// <summary>The clause the suspension attestation carries about the sessions the suspension ended.</summary>
    /// <param name="report">What the suspension cascade's subscribers reported back.</param>
    /// <returns>
    /// The clause, ending in <c>"; "</c> so it joins the line — never empty, because all four readings
    /// are things an operator has to be able to tell apart, and an omitted clause would be a fifth
    /// meaning nobody chose.
    /// </returns>
    public static string Describe(AccountSuspensionCascadeReport report)
    {
        if (!report.SessionCullReported)
        {
            return "the panel asked the host to end the account's open transfer sessions, and no "
                + "module reported having done so, so this line cannot say that any were ended; ";
        }

        if (report.SessionsEnded is not { } ended)
        {
            return "a suspension also ends the account's open transfer sessions, and the host's answer "
                + "carried no count of them, so this line cannot say how many were ended, or that any "
                + "were; ";
        }

        return ended == 0
            ? "a suspension also ends the account's open transfer sessions, and the host found none of "
                + "this account's to end; "
            : $"a suspension also ends the account's open transfer sessions, and the host ended {ended} "
                + "of them, cutting whatever they were transferring and leaving any partial file in the "
                + "account's home; ";
    }
}
