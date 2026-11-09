using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Where the module loader records what it did, and what it refused, in the panel's audit journal. The
/// contract is the Sdk's because the loader that implements <see cref="IModuleLoader"/> is a separate
/// package that cannot reference the Host; the Host answers it, so the entry is built in one place that
/// decides what an entry of this kind may carry.
/// </summary>
/// <remarks>
/// <para>
/// The signature carries no free text on purpose. A package is untrusted until it has been verified, and
/// a refusal is recorded about a package that FAILED verification, so the module id and version arriving
/// here may be anything the package's author typed. The Host's journal writes an id or version only
/// when it is well formed, and records the refusal as a machine code, never as the text of an
/// exception, a path or tool output (rules/security.md item 8).
/// </para>
/// <para>
/// Refusals are recorded as <see cref="MarketplaceAuditActions.ModuleRefused"/> with
/// <c>succeeded = false</c>; the loader owes one such entry for every package it turns away.
/// </para>
/// </remarks>
public interface IModuleLoaderAudit
{
    /// <summary>Records one step of the module lifecycle.</summary>
    /// <param name="action">One of <see cref="MarketplaceAuditActions"/>.</param>
    /// <param name="moduleId">The module's id as its manifest spells it, or null when no manifest could be read.</param>
    /// <param name="version">The module's version as its manifest spells it, or null when none could be read.</param>
    /// <param name="reason">Why the step failed or the module was refused; null for a step that succeeded.</param>
    /// <param name="succeeded">Whether the step took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    Task RecordAsync(
        string action,
        string? moduleId,
        string? version,
        Error? reason,
        bool succeeded,
        CancellationToken cancellationToken);
}
