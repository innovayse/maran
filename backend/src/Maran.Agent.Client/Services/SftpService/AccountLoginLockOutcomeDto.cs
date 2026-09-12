namespace Maran.Agent.Client.Services.SftpService;

/// <summary>What the agent did when it was asked to lock or unlock every login of one account.</summary>
/// <remarks>
/// <para>
/// <b>Why this record exists rather than a <c>bool</c>.</b> The locking direction also ends every
/// session the account already has open — a privileged action, because it cuts a transfer in flight
/// and leaves the partial file behind — and the agent counts the processes it signalled. Until this
/// record that figure reached only the agent's own log, while the panel's confirmation dialog was
/// already promising the operator that the transfer would be cut. So the panel promised one thing
/// before the act and its own attestation could only say it had ASKED. This record is the value that
/// lets the record say what the promise said.
/// </para>
/// <para>
/// <b>It is an event, not a state, which is why nothing can re-read it.</b>
/// <c>GetAccountSuspensionState</c> observes the host afterwards and is how every other fact on the
/// attestation is obtained; a cull has no afterwards to observe. If this answer is dropped the
/// number is gone.
/// </para>
/// </remarks>
/// <param name="SessionsEnded">
/// How many of the account's processes the agent signalled, or <c>null</c> when the host's answer
/// carried no count.
///
/// <para>
/// Three readings, and the reason none may be collapsed into another. A number above zero is a
/// report of work done. <c>0</c> is a completeness claim — the agent culled and <c>pkill</c> matched
/// no process, which is the state a suspension is trying to reach. <c>null</c> is neither: either
/// the agent predates the wire field, or this was the UNLOCK direction, which ends no session and so
/// has nothing to count. Rendering <c>null</c> as <c>0</c> would put the completeness claim in front
/// of an operator over an answer the host never gave, which is the defect the
/// unmanaged-login count of <c>GetAccountSuspensionState</c> already pays for one field along.
/// </para>
/// <para>
/// It is <c>null</c> and not a sentinel because the wire itself is: <c>sessions_ended</c> is an
/// <c>optional uint32</c>, so absence is a fact of the message rather than something the panel has
/// to infer from an unrelated field of the same answer. A FAILED cull is not one of the three — the
/// agent answers an error there and no outcome is built at all, because some of the account's
/// processes may have been signalled and some may not.
/// </para>
/// </param>
public sealed record AccountLoginLockOutcomeDto(uint? SessionsEnded);
