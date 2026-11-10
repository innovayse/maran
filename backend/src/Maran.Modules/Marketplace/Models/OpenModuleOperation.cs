using Maran.Modules.Marketplace.Domain.Enums;

namespace Maran.Modules.Marketplace.Models;

/// <summary>An install, update, remove or rollback that has a task and has not finished, as the module's claim row records it.</summary>
/// <param name="Operation">Which operation.</param>
/// <param name="Version">The version it installs, or the installed version a remove or rollback acts on.</param>
/// <param name="TaskId">The task that carries its progress.</param>
public sealed record OpenModuleOperation(MarketplaceOperation Operation, string? Version, Guid TaskId);
