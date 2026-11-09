using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The panel's runtime view of its modules: those composed with it and those that came from packages,
/// each with the state it is in. The Host implements it; a consumer that must know what is installed
/// asks this, never the static list the Host composes at start-up.
/// </summary>
/// <remarks>
/// A question and not a property because the state of a packaged module is the loader's to report, and
/// the loader may have to read it from disk. Which modules are composed does not change while the
/// process runs (a package activates at the next start); only their state does.
/// </remarks>
public interface IModuleRegistry
{
    /// <summary>Lists every module the panel holds, each with its current state.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Composed modules first, in composition order, then modules that were refused.</returns>
    Task<IReadOnlyList<RegisteredModule>> ListAsync(CancellationToken cancellationToken);
}
