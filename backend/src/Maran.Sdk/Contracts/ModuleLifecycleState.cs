namespace Maran.Sdk.Contracts;

/// <summary>
/// Where a module stands in the panel right now. Read by the modules catalogue and by the loader's
/// own bookkeeping; a state is a fact about the running panel, never something a module declares.
/// </summary>
public enum ModuleLifecycleState
{
    /// <summary>The package is verified and kept on disk, and the module has not been activated yet.</summary>
    Installed,

    /// <summary>The module is composed and serving: its routes answer and its handlers run.</summary>
    Active,

    /// <summary>
    /// The module is composed but not serving what its licence does not cover (a lapsed lease), so the
    /// rest of the panel keeps running and the module answers with its licence response.
    /// </summary>
    Degraded,

    /// <summary>The module could not be activated; the failure is carried beside the state.</summary>
    Failed,

    /// <summary>The module was unloaded and its package removed; kept only as a history entry.</summary>
    Removed,
}
