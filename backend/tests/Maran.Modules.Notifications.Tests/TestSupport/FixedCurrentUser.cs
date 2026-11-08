using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>An <see cref="ICurrentUser"/> that is whichever caller the test names, so two administrators can share a database.</summary>
public sealed class FixedCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Creates the user.</summary>
    /// <param name="caller">The caller every read of <see cref="Caller"/> answers.</param>
    public FixedCurrentUser(Caller caller)
    {
        Caller = caller;
    }

    /// <summary>An administrator with the given id.</summary>
    /// <param name="userId">The administrator's id.</param>
    /// <returns>The user.</returns>
    public static FixedCurrentUser Administrator(Guid userId)
    {
        return new FixedCurrentUser(new AdministratorCaller(userId, "admin"));
    }

    /// <summary>A caller nobody signed in as.</summary>
    /// <returns>The user.</returns>
    public static FixedCurrentUser Anonymous()
    {
        return new FixedCurrentUser(AnonymousCaller.Instance);
    }

    /// <summary>A customer login, who must never see the operator's feed.</summary>
    /// <returns>The user.</returns>
    public static FixedCurrentUser Customer()
    {
        return new FixedCurrentUser(new CustomerCaller(Guid.NewGuid(), "customer", Guid.NewGuid()));
    }
}
