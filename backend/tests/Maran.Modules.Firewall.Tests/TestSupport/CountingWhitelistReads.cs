using Maran.Modules.Firewall.Domain.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>
/// Counts how many whitelist rows a test's queries have materialised, which is how a test asserts
/// that the reconciler reads the whitelist ONCE for a pass rather than once per episode.
/// </summary>
/// <remarks>
/// A materialization interceptor rather than a command interceptor: the in-memory provider issues no
/// commands, so counting rows is the only measurement available on it — and with a single whitelist
/// row it is exact, because one read of a one-row table materialises one entity.
/// </remarks>
public sealed class CountingWhitelistReads : IMaterializationInterceptor
{
    /// <summary>How many whitelist rows have been materialised since this interceptor was created.</summary>
    public int Count { get; private set; }

    /// <inheritdoc />
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is WhitelistEntry)
        {
            Count++;
        }

        return entity;
    }
}
