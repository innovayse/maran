namespace Maran.Agent.Client.Services.DbService;

/// <summary>Why the grant-table repair left a row exactly as it found it.</summary>
/// <remarks>
/// The agent's own closed set, restated without the wire's zero value: a reason the agent did not name
/// is not a fifth reason, it is a reply the panel cannot read, and the client answers it with
/// <c>AgentInvalidResponse</c> instead of inventing a row with an empty explanation.
/// </remarks>
public enum AgentGrantRefusal
{
    /// <summary>The grant is for a host other than <c>localhost</c>, the only host this panel grants from.</summary>
    HostIsNotLocalhost,

    /// <summary>The database and user names are not a pair this panel could have created.</summary>
    NotThePanelsNaming,

    /// <summary>The row does not hold exactly the all-privileges grant this panel issues.</summary>
    UnrecognisedPrivileges,

    /// <summary>The stored pattern is escaped in a form this panel never writes.</summary>
    PartiallyOrUnfamiliarlyEscaped,
}
