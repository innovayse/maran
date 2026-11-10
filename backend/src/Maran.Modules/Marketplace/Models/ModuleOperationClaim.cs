namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// The receipt for a successful <c>ModuleOperationGate.AcquireAsync</c>: proof that the holder, and only the
/// holder, may run an operation on one module until it releases the claim or the claim goes stale.
/// </summary>
/// <param name="Id">The claim's identity; releasing addresses this, so a taken-over claim's old owner cannot release the new one.</param>
/// <param name="ModuleId">The module the claim is on.</param>
public sealed record ModuleOperationClaim(Guid Id, string ModuleId);
