using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>A principal that is a customer acting for one account.</summary>
public sealed class CustomerCurrentUser : ICurrentUser
{
    /// <summary>The customer.</summary>
    public Caller Caller { get; }

    /// <summary>Creates a customer of the given account.</summary>
    /// <param name="accountId">The account the customer acts for.</param>
    public CustomerCurrentUser(Guid accountId)
    {
        Caller = new CustomerCaller(Guid.NewGuid(), "customer", accountId);
    }
}
