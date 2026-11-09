using Maran.Modules.Fleet.Domain.Enums;
using Maran.Modules.Fleet.Domain.ValueObjects;
using Maran.Modules.Fleet.Resources;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Fleet.Domain.Entities;

/// <summary>
/// One server the panel manages: the host it runs on, or a node an administrator enrolled. The row is
/// the panel's record of WHAT it was told and WHAT an administrator approved; it is never a record of
/// what the server is doing, which only a health observation can say.
/// </summary>
/// <remarks>
/// <para>
/// <b>The state machine is the security property.</b> A node is created <see cref="ServerState.Pending"/>
/// and the panel dials a server only when <see cref="MayBeDialled"/> says so, which a pending one never
/// does. The way out of pending is <see cref="Approve"/>, and it takes the fingerprint the administrator
/// TYPED, which must equal the one the host presented: a token thief who enrols a host of their own
/// cannot get it approved without an administrator comparing a fingerprint that came from the thief's
/// terminal. Every other transition refuses from a state it was not designed for rather than coercing
/// the row, so the only way to a dialled state is through the approval.
/// </para>
/// <para>
/// <b>One field, one meaning.</b> <see cref="Address"/> and <see cref="Port"/> are where the panel dials
/// a node and are null for the local server, which is reached over the agent's unix socket and has no
/// address; <see cref="Fingerprint"/> is the pinned identity of a node and is null until the
/// host has presented a certificate, and always null for the local server, which has no certificate.
/// A null here is "this kind of server has none" or "not yet", never "unknown", and the factories are
/// the only place that distinction is decided.
/// </para>
/// <para>
/// It carries no <c>AccountId</c> and is not tenant data: only an administrator reaches the registry,
/// and a customer never names a server (the placement of an account is read from the account's own
/// row). <c>TenantScopeTests</c> therefore does not ask for a query filter on it.
/// </para>
/// </remarks>
public sealed class Server
{
    /// <summary>The longest name a server is given, in characters. Sized for a list cell, not for a sentence.</summary>
    public const int NameMaxLength = 64;

    /// <summary>The longest address a node is dialled at: the longest host name DNS allows.</summary>
    public const int AddressMaxLength = HostNameRule.MaximumLength;

    /// <summary>The lowest port a node's agent may listen on.</summary>
    public const int PortMinimum = 1;

    /// <summary>The highest port a node's agent may listen on.</summary>
    public const int PortMaximum = 65535;

    /// <summary>The row's identity; <see cref="FleetServers.LocalId"/> for the local server.</summary>
    public Guid Id { get; private set; }

    /// <summary>The name an administrator knows the server by, shown in the selector and in lists.</summary>
    public string Name { get; private set; }

    /// <summary>Whether this is the panel's own host or an enrolled node.</summary>
    public ServerKind Kind { get; private set; }

    /// <summary>
    /// The host name or IP address a node's agent is dialled at; null for the local server, which is
    /// reached over a unix socket.
    /// </summary>
    public string? Address { get; private set; }

    /// <summary>The TCP port a node's agent listens on; null for the local server.</summary>
    public int? Port { get; private set; }

    /// <summary>
    /// The SHA-256 fingerprint of the certificate the node presented at enrolment, as 64 lowercase
    /// hexadecimal digits; null until a host has enrolled and always null for the local server. Once an
    /// administrator approves, this is the pin the panel checks every connection against.
    /// </summary>
    public string? Fingerprint { get; private set; }

    /// <summary>Where the server stands; see <see cref="ServerState"/>.</summary>
    public ServerState State { get; private set; }

    /// <summary>When the row was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When an administrator approved the node's fingerprint; null until then and for the local server.
    /// </summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>
    /// True when the panel may open a connection to this server. False for a pending server (not yet
    /// approved) and for a disabled one; true for the local server and every approved, enabled node,
    /// including one that is currently unreachable, because dialling it is how the panel learns that
    /// it has come back.
    /// </summary>
    public bool MayBeDialled
    {
        get
        {
            return State is not ServerState.Pending and not ServerState.Disabled;
        }
    }

    /// <summary>
    /// Creates the row for the server the panel runs on. Its state is <see cref="ServerState.Online"/>
    /// by definition: if this process is answering, its own host is up.
    /// </summary>
    /// <param name="name">What the server is called; the host's name unless an administrator renames it.</param>
    /// <param name="at">When the row is created.</param>
    private Server(string name, DateTimeOffset at)
    {
        Id = FleetServers.LocalId;
        Name = name;
        Kind = ServerKind.Local;
        State = ServerState.Online;
        CreatedAt = at;
    }

    /// <summary>
    /// Creates the row for a node an administrator declared, pending until a host enrols and is approved.
    /// </summary>
    /// <param name="id">The new server's identity.</param>
    /// <param name="name">What the server is called; already validated, 1 to <see cref="NameMaxLength"/> long.</param>
    /// <param name="address">Where its agent is dialled; already validated as a host name or address.</param>
    /// <param name="port">The agent's TCP port.</param>
    /// <param name="at">When the row is created.</param>
    private Server(Guid id, string name, string address, int port, DateTimeOffset at)
    {
        Id = id;
        Name = name;
        Kind = ServerKind.Node;
        Address = address;
        Port = port;
        State = ServerState.Pending;
        CreatedAt = at;
    }

    /// <summary>EF Core's constructor; nothing else may build an entity in an undefined state.</summary>
    private Server()
    {
        Name = string.Empty;
    }

    /// <summary>Creates the row for the server the panel runs on.</summary>
    /// <param name="name">What the server is called; not blank, at most <see cref="NameMaxLength"/> long.</param>
    /// <param name="at">When the row is created.</param>
    /// <returns>
    /// The local server, in <see cref="ServerState.Online"/>, with the well-known <see cref="FleetServers.LocalId"/>.
    /// </returns>
    public static Server CreateLocal(string name, DateTimeOffset at)
    {
        RequireName(name);
        return new Server(name, at);
    }

    /// <summary>Creates the row for a node an administrator declared.</summary>
    /// <param name="id">The new server's identity.</param>
    /// <param name="name">What the server is called; not blank, at most <see cref="NameMaxLength"/> long.</param>
    /// <param name="address">Where its agent is dialled; not blank, at most <see cref="AddressMaxLength"/>.</param>
    /// <param name="port">The agent's TCP port, <see cref="PortMinimum"/> to <see cref="PortMaximum"/>.</param>
    /// <param name="at">When the row is created.</param>
    /// <returns>A <see cref="ServerKind.Node"/> in <see cref="ServerState.Pending"/> with no fingerprint.</returns>
    /// <remarks>
    /// The arguments are checked here as well as by the command's validator, but a failure here is a bug
    /// in the caller and is an exception, not a result: a person's mistake is the validator's 400.
    /// <see cref="FleetServers.LocalId"/> is refused as an identity so a node can never be the local server.
    /// </remarks>
    public static Server Declare(Guid id, string name, string address, int port, DateTimeOffset at)
    {
        if (id == Guid.Empty || id == FleetServers.LocalId)
        {
            throw new ArgumentException("A node has an identity of its own.", nameof(id));
        }

        RequireName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(address.Length, AddressMaxLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, PortMinimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, PortMaximum);

        return new Server(id, name, address, port, at);
    }

    /// <summary>Gives the server another name.</summary>
    /// <param name="name">The new name; not blank and at most <see cref="NameMaxLength"/> characters.</param>
    public void Rename(string name)
    {
        RequireName(name);
        Name = name;
    }

    /// <summary>
    /// Records the certificate a host presented when it enrolled, which is what the administrator is then
    /// asked to approve. A second enrolment of a still-pending node replaces the first: the administrator
    /// runs the command on the node again, and nothing was approved against the earlier certificate.
    /// </summary>
    /// <param name="fingerprint">The fingerprint of the presented certificate.</param>
    /// <returns>
    /// Success, or a conflict when the server is not a pending node: an approved node's pin is changed
    /// only by removing the node and enrolling it again, never by a host that asks.
    /// </returns>
    public Result RecordEnrolment(CertificateFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        if (Kind != ServerKind.Node || State != ServerState.Pending)
        {
            return Refused();
        }

        Fingerprint = fingerprint.Value;
        return Result.Ok();
    }

    /// <summary>
    /// Approves the node: the administrator has read the fingerprint on the node's own terminal and typed
    /// it, and it is the one the host presented. From here the panel dials the node.
    /// </summary>
    /// <param name="typed">The fingerprint the administrator typed.</param>
    /// <param name="at">When the approval happened.</param>
    /// <returns>
    /// Success, or: a conflict when the server is not a pending node; a conflict when no host has
    /// enrolled yet, so there is nothing to approve; a validation failure when the typed fingerprint
    /// differs from the presented one.
    /// </returns>
    /// <remarks>
    /// The node is left <see cref="ServerState.Unreachable"/>, not online: an approval is a decision, not
    /// an observation, and the first successful health poll is what moves it to
    /// <see cref="ServerState.Online"/>. Showing a node as online that nothing has reached would be the
    /// panel agreeing with itself.
    /// </remarks>
    public Result Approve(CertificateFingerprint typed, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(typed);

        if (Kind != ServerKind.Node || State != ServerState.Pending)
        {
            return Refused();
        }

        if (!CertificateFingerprint.TryParse(Fingerprint, out var presented) || presented is null)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.ServerNotEnrolled), ErrorType.Conflict));
        }

        if (!presented.Matches(typed))
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.ServerFingerprintMismatch), ErrorType.Validation));
        }

        State = ServerState.Unreachable;
        ApprovedAt = at;
        return Result.Ok();
    }

    /// <summary>Switches an approved node off: it keeps its pin and is no longer dialled.</summary>
    /// <returns>
    /// Success, a conflict when the server is pending or already disabled, or
    /// <c>ServerLocalImmutable</c> for the local server, which cannot be switched off from the panel
    /// running on it.
    /// </returns>
    public Result Disable()
    {
        if (Kind == ServerKind.Local)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.ServerLocalImmutable), ErrorType.Conflict));
        }

        if (State is ServerState.Pending or ServerState.Disabled)
        {
            return Refused();
        }

        State = ServerState.Disabled;
        return Result.Ok();
    }

    /// <summary>Switches a disabled node back on, without asking for approval again: its pin was kept.</summary>
    /// <returns>
    /// Success, or a conflict when the server is not disabled. The node is left
    /// <see cref="ServerState.Unreachable"/> until the next health poll observes it, for the reason
    /// <see cref="Approve"/> gives.
    /// </returns>
    public Result Enable()
    {
        if (State != ServerState.Disabled)
        {
            return Refused();
        }

        State = ServerState.Unreachable;
        return Result.Ok();
    }

    /// <summary>The failure for a transition the current state does not allow.</summary>
    /// <returns>A conflict: the request is well formed and the server's state is what is wrong for it.</returns>
    private static Result Refused()
    {
        return Result.Fail(Error.Of(nameof(ErrorMessages.ServerStateTransitionInvalid), ErrorType.Conflict));
    }

    /// <summary>Refuses a name no caller should have passed.</summary>
    /// <param name="name">The candidate name.</param>
    private static void RequireName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, NameMaxLength);
    }
}
