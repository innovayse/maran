namespace Maran.Sdk.Events;

/// <summary>
/// What the subscribers of one <see cref="AccountSuspending"/> report back about the privileged
/// actions they took, carried on the message so the publisher can attest to them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this solves.</b> <see cref="AccountSuspending"/> is fan-out and fire-and-forget: it
/// has three subscribers today, any number tomorrow including a marketplace module this assembly was
/// never compiled knowing about, and no return value. One of those subscribers drives an agent
/// operation that does something an operator has to be told about — locking the account's transfer
/// logins also ENDS the sessions already open, cutting a transfer in flight and leaving the partial
/// file in the customer's home — and the agent counts the processes it signalled. The publisher
/// renders the operator's attestation and had no way to learn that count, so the panel warned the
/// operator about the cost before the act and its own record could only say it had ASKED.
/// </para>
/// <para>
/// <b>Why a report on the message and not the alternatives.</b> A request/response event was the
/// obvious shape and is wrong: Wolverine's <c>InvokeAsync&lt;TResponse&gt;</c> wants exactly one
/// handler, so giving this event a result would turn a cascade with three subscribers into a call
/// with one and make the absence of a subscriber an error rather than a fact. An ambient scoped
/// service that the subscriber writes and the publisher reads would work but hides the hand-off:
/// nothing at either call site says the value travels, its lifetime is the DI scope rather than the
/// operation, and a scope that handles two suspensions carries the first one's number into the
/// second. Letting the publisher call the agent itself is worse still — it would put the one door to
/// the root process in a second module's manifest, which is the reach rules/security.md item 13
/// exists to narrow, and it would cull twice. Putting the count on the state query the attestation
/// already makes is not available either: a cull is an event with no afterwards to observe, and the
/// agent MUST stay stateless (rules/architecture.md "Agent"), so it cannot remember one.
/// </para>
/// <para>
/// So the report rides with the message. Its lifetime is exactly the operation's, it is declared in
/// the contract surface both sides already depend on so no module reads another's schema, and it
/// crosses no new boundary: the publisher already awaits <c>InvokeAsync</c> inline, and the whole
/// design of this event depends on that — a handler that throws aborts the suspension. By the time
/// that await returns, every subscriber that exists has run and written what it has.
/// </para>
/// <para>
/// <b>Stated limit, and which way it fails.</b> This works because the cascade executes INLINE, in
/// process, on the publisher's own object. Route this message to a durable or external transport and
/// the subscriber writes into a deserialized copy, so the publisher reads a report nobody filled in.
/// That is the understating direction by construction: an unfilled report is indistinguishable from a
/// cascade no subscriber answered, which is a state this type already has a reading for and which the
/// attestation renders as a refusal to claim anything. A silent wrong NUMBER is not reachable — the
/// only way to get a number out of this type is for a subscriber to have put one in.
/// </para>
/// <para>
/// <b>Why a mutable class in an Sdk of records.</b> Because it is a collector and not a value: it is
/// written by the subscribers and read once by the publisher, so record equality and <c>with</c>
/// expressions would be meaningless on it. It is declared as a non-positional property of
/// <see cref="AccountSuspending"/> for the same reason — a collector is not part of a message's
/// identity.
/// </para>
/// </remarks>
public sealed class AccountSuspensionCascadeReport
{
    /// <summary>Guards the writes, because subscriber order and threading are the bus's business.</summary>
    /// <remarks>
    /// Wolverine runs this cascade's handlers in sequence today, and nothing here relies on that. A
    /// lock over two fields written together is what makes "reported" and "the number" one fact
    /// rather than two that can be read half-updated.
    /// </remarks>
    private readonly object _gate = new();

    /// <summary>Whether any subscriber answered about the session cull at all.</summary>
    private bool _sessionCullReported;

    /// <summary>The count the answering subscriber carried, which may itself be absent.</summary>
    private uint? _sessionsEnded;

    /// <summary>Whether a subscriber reported on the session cull.</summary>
    /// <remarks>
    /// <c>false</c> is a real and separate fact, not a default to be read as zero: no module that
    /// ends sessions was composed, or none ran. The publisher may then say only that it asked. This
    /// is the half <see cref="AccountSuspending"/>'s own remarks are about — a subscriber that does
    /// not exist cannot throw, so a completed cascade is not evidence that anything happened.
    /// </remarks>
    public bool SessionCullReported
    {
        get
        {
            lock (_gate)
            {
                return _sessionCullReported;
            }
        }
    }

    /// <summary>How many sessions were ended, when a subscriber reported and the host gave a number.</summary>
    /// <remarks>
    /// <c>null</c> alongside <see cref="SessionCullReported"/> being <c>true</c> means a subscriber
    /// ran and the HOST's answer carried no count — an agent predating the wire field. Together the
    /// two properties carry three facts a reader must keep apart: a number, a measured zero (the cull
    /// ran and matched no process), and no number at all. Collapsing any pair of them would put a
    /// completeness claim on an operator's screen over an answer nobody gave.
    /// </remarks>
    public uint? SessionsEnded
    {
        get
        {
            lock (_gate)
            {
                return _sessionsEnded;
            }
        }
    }

    /// <summary>Records what a subscriber's session cull did.</summary>
    /// <param name="sessionsEnded">
    /// How many of the account's processes were signalled, or <c>null</c> when the host's answer
    /// carried no count. Pass the host's answer through unchanged: substituting zero for an absent
    /// count is the one thing this type exists to prevent.
    /// </param>
    /// <remarks>
    /// Last writer wins on the number, and that is deliberate rather than unconsidered: there is one
    /// operation that ends an account's sessions, so a second caller here would be a second module
    /// driving the same agent rpc — a duplicate the publisher must not average, sum, or silently
    /// prefer the first of. <see cref="SessionCullReported"/> stays <c>true</c> either way, so the
    /// publisher still never claims a silence it did not achieve.
    /// </remarks>
    public void ReportSessionCull(uint? sessionsEnded)
    {
        lock (_gate)
        {
            _sessionCullReported = true;
            _sessionsEnded = sessionsEnded;
        }
    }
}
