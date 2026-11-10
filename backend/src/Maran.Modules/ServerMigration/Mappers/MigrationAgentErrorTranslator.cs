using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Resources;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// The single place a failed call to a remote server becomes something this module may carry: one of its own error
/// codes, and, for the refusals that describe the server and not the agent, the kind of failure the registry records.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reason the agent states decides the code; the agent's own words never travel.</b> The client has already
/// logged the agent's text redacted, so nothing is attached here. A refusal the panel can act on — the host key
/// differs from the pinned one, the login was refused, the server did not answer, the address is one the agent will
/// not connect to — has a code and a sentence of its own, because each tells the administrator a different thing to
/// do. Anything else is either the agent not being reachable (<see cref="ErrorType.Unavailable"/>, the same answer
/// every agent client gives an outage) or a failure of the work itself.
/// </para>
/// <para>
/// A failure that carries a reason is a fact about the SERVER and is recorded on its row
/// (<see cref="RecordedFailureOf"/>); one that carries none is a fact about the panel's own agent and is not, so an
/// outage of the agent never marks a healthy server as unreachable.
/// </para>
/// </remarks>
public static class MigrationAgentErrorTranslator
{
    /// <summary>Re-codes a failed call as this module's own error.</summary>
    /// <param name="agentError">The failure the agent client returned; only its kind is read.</param>
    /// <param name="reason">Why the server refused, when the agent stated it.</param>
    /// <returns>The error this module answers with.</returns>
    public static Error Translate(Error agentError, SourceFailureReason reason)
    {
        ArgumentNullException.ThrowIfNull(agentError);

        return reason switch
        {
            SourceFailureReason.HostKeyChanged =>
                Error.Of(nameof(ErrorMessages.RemoteServerHostKeyChanged), ErrorType.Conflict),
            SourceFailureReason.AuthRefused =>
                Error.Of(nameof(ErrorMessages.RemoteServerAuthRefused), ErrorType.Conflict),
            SourceFailureReason.Unreachable =>
                Error.Of(nameof(ErrorMessages.RemoteServerUnreachable), ErrorType.Unavailable),
            SourceFailureReason.AddressRefused =>
                Error.Of(nameof(ErrorMessages.RemoteServerAddressRefused), ErrorType.Validation),
            _ => agentError.Type == ErrorType.Unavailable
                ? Error.Of(nameof(ErrorMessages.MigrationAgentUnavailable), ErrorType.Unavailable)
                : Error.Of(nameof(ErrorMessages.MigrationDiscoveryFailed), ErrorType.Failure),
        };
    }

    /// <summary>The failure the registry records on a server for a reason the agent stated.</summary>
    /// <param name="reason">Why the server refused.</param>
    /// <returns>The kind to record, or <c>null</c> when the failure says nothing about the server.</returns>
    public static RemoteFailureKind? RecordedFailureOf(SourceFailureReason reason)
    {
        return reason switch
        {
            SourceFailureReason.HostKeyChanged => RemoteFailureKind.HostKeyChanged,
            SourceFailureReason.AuthRefused => RemoteFailureKind.AuthRefused,
            SourceFailureReason.Unreachable => RemoteFailureKind.Unreachable,
            SourceFailureReason.AddressRefused => RemoteFailureKind.AddressRefused,
            _ => null,
        };
    }
}
