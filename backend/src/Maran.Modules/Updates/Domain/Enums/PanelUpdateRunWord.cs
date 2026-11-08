namespace Maran.Modules.Updates.Domain.Enums;

/// <summary>
/// What the root update tells the panel about a run an administrator requested: the one stage it reports
/// while running, and the ways it can end. A closed set, spelled on the command line by the root
/// <c>maran</c> command (issue #382).
/// </summary>
public enum PanelUpdateRunWord
{
    /// <summary>A release was decided on and its installation is beginning.</summary>
    Installing,

    /// <summary>A newer release was installed and the health check passed.</summary>
    Applied,

    /// <summary>The panel was already on the newest release; nothing was installed.</summary>
    Unchanged,

    /// <summary>The update was refused before it changed anything.</summary>
    Refused,

    /// <summary>The release failed its health check and the previous one was restored.</summary>
    RolledBack,

    /// <summary>The release failed and the previous one did not become healthy either.</summary>
    RollbackFailed,

    /// <summary>The run was interrupted after the release was switched.</summary>
    Interrupted,
}
