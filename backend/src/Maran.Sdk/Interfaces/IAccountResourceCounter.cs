using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// A read-only window through which a module answers how many of its own resources an account owns,
/// so the Accounts module can show usage beside the plan's limits without reading another module's
/// schema (rules/architecture.md).
/// </summary>
/// <remarks>
/// Implemented by the OWNING module and consumed as <c>IEnumerable&lt;IAccountResourceCounter&gt;</c>:
/// a module that is not composed registers no counter, and its kind is simply absent from the
/// summary rather than reported as zero. The count is taken through the owning module's
/// tenant-scoped context, so a caller sees only what its own scope allows.
/// </remarks>
public interface IAccountResourceCounter
{
    /// <summary>The one kind of resource this counter answers for.</summary>
    AccountResourceKind Kind { get; }

    /// <summary>Counts the account's resources of <see cref="Kind"/>.</summary>
    /// <param name="accountId">The account whose resources are counted.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <returns>The count, or an explicit unavailable answer when the source of truth did not respond.</returns>
    Task<AccountResourceCount> CountAsync(Guid accountId, CancellationToken cancellationToken);
}
