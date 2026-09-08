namespace Maran.Agent.Client.Services.BackupService;

/// <summary>Why a listed artifact could not be described.</summary>
/// <remarks>
/// Three kinds and not one, because they call for three different reactions: a sidecar that will not
/// parse is probably damage, one written at a version this agent does not know is probably a routine
/// skew, and a directory or a symbolic link wearing an artifact's name is neither.
/// </remarks>
public enum AgentBackupUnreadableKind
{
    /// <summary>
    /// The wire named a kind this panel build has no member for — a newer agent. Kept distinct from
    /// the three known kinds so that "we do not know why" is never rendered as "it is damaged".
    /// </summary>
    Unknown = 0,

    /// <summary>The sidecar was missing, or its JSON did not parse into any shape the agent writes.</summary>
    Corrupt = 1,

    /// <summary>The sidecar parsed but named a version the agent will not read past.</summary>
    UnknownVersion = 2,

    /// <summary>
    /// Something wearing an artifact's name is not a regular file. Listed rather than skipped: a
    /// directory is an entry retention can never prune, and a symbolic link is one a restore would
    /// read through.
    /// </summary>
    NotARegularFile = 3,
}
