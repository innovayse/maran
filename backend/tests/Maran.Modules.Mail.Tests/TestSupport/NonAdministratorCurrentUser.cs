using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// A principal whose tenant filter shows the Mail module NO rows: a reseller, an API key or an anonymous
/// (unattended) caller. The local submission binding is a system action, and these are the callers an
/// administrator-scoped test cannot imitate.
/// </summary>
public sealed class NonAdministratorCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; }

    private NonAdministratorCurrentUser(Caller caller)
    {
        Caller = caller;
    }

    /// <summary>A reseller login, which owns accounts and has none of its own.</summary>
    /// <returns>The principal.</returns>
    public static NonAdministratorCurrentUser Reseller()
    {
        return new NonAdministratorCurrentUser(new ResellerCaller(Guid.NewGuid(), "reseller", Guid.NewGuid()));
    }

    /// <summary>The anonymous caller of unattended work (a scheduled job, an event with no request).</summary>
    /// <returns>The principal.</returns>
    public static NonAdministratorCurrentUser Anonymous()
    {
        return new NonAdministratorCurrentUser(AnonymousCaller.Instance);
    }
}
