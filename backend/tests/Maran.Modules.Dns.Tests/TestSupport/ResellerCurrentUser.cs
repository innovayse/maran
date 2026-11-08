using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>A principal that is a reseller, whose context shows no zone.</summary>
public sealed class ResellerCurrentUser : ICurrentUser
{
    /// <summary>The reseller.</summary>
    public Caller Caller { get; } = new ResellerCaller(Guid.NewGuid(), "reseller", Guid.NewGuid());
}
