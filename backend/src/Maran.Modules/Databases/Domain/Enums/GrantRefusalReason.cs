namespace Maran.Modules.Databases.Domain.Enums;

/// <summary>Why the grant-table repair left a row exactly as it found it.</summary>
/// <remarks>
/// The agent's closed set, one member per reason the panel has words for. A reason the agent reports
/// that is not here never reaches this type: the agent client refuses the whole reply, so a row is
/// never shown with an explanation the panel invented. Every member needs a name and an advice entry
/// in <c>DisplayNames</c>, and the module's tests walk this enum to prove it.
/// </remarks>
public enum GrantRefusalReason
{
    /// <summary>The grant is for a host other than <c>localhost</c>, the only host this panel grants from.</summary>
    HostIsNotLocalhost = 1,

    /// <summary>The database and user names are not a pair this panel could have created.</summary>
    NotThePanelsNaming = 2,

    /// <summary>The row does not hold exactly the all-privileges grant this panel issues.</summary>
    UnrecognisedPrivileges = 3,

    /// <summary>The stored pattern is escaped in a form this panel never writes.</summary>
    PartiallyOrUnfamiliarlyEscaped = 4,
}
