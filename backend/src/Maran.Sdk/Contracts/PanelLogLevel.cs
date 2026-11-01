namespace Maran.Sdk.Contracts;

/// <summary>
/// The lowest severity the panel's own logger writes, as an administrator chooses it at runtime
/// (issue #131).
/// </summary>
/// <remarks>
/// <para>
/// Six members because the logger has six, and the mapping between the two is total in both
/// directions: a level read back from the running logger is always one of these, so the settings
/// screen never has to show a value it cannot name. Fewer members would force a lie the first time
/// an operator set <c>Verbose</c> from <c>panel.env</c> and the screen had to report it.
/// </para>
/// <para>
/// The level governs the panel's default. The per-namespace overrides an operator writes into
/// configuration (the framework's request logging, the ORM's SQL text) are not affected, so choosing
/// <see cref="Verbose"/> does not start writing every SQL statement with its parameters into the
/// journal.
/// </para>
/// </remarks>
public enum PanelLogLevel
{
    /// <summary>Everything, including the finest-grained tracing; for a short investigation only.</summary>
    Verbose = 0,

    /// <summary>Diagnostic detail an operator reads while something is being investigated.</summary>
    Debug = 1,

    /// <summary>The normal level: what the panel did, one line per notable event.</summary>
    Information = 2,

    /// <summary>Only what may need an operator's attention.</summary>
    Warning = 3,

    /// <summary>Only failures.</summary>
    Error = 4,

    /// <summary>Only failures that stop the panel.</summary>
    Fatal = 5,
}
