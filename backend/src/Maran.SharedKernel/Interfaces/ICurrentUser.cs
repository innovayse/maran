using Maran.SharedKernel.Callers;

namespace Maran.SharedKernel.Interfaces;

/// <summary>The caller of the current request or message.</summary>
/// <remarks>
/// <see cref="Caller"/> is the one fact, and a member exists only on the case that has it: an account
/// id on a <see cref="CustomerCaller"/>, a user id on any <see cref="PrincipalCaller"/>, the operator
/// viewing as a login on <see cref="PrincipalCaller.ViewedBy"/>. A consumer switches on it, so a case
/// it forgot is a compile-time gap instead of a stand-in value (<see cref="Guid.Empty"/>, an empty
/// name, a missing account) read as a real one.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>
    /// Who this request or message runs as: <see cref="AnonymousCaller"/>, <see cref="AdministratorCaller"/>,
    /// <see cref="CustomerCaller"/> or <see cref="AccountlessCustomerCaller"/>.
    /// </summary>
    Caller Caller { get; }
}
