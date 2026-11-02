using Maran.SharedKernel.Callers;

namespace Maran.SharedKernel.Interfaces;

/// <summary>The caller of the current request or message.</summary>
/// <remarks>
/// <see cref="Caller"/> is the one fact. The members below it are the old per-field answers, derived
/// from it with their old meaning (a stand-in for "nobody"), kept as default members only while
/// consumers move to switching on <see cref="Caller"/>; they are removed once none reads them.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>
    /// Who this request or message runs as: <see cref="AnonymousCaller"/>, <see cref="AdministratorCaller"/>
    /// or <see cref="CustomerCaller"/>.
    /// </summary>
    Caller Caller { get; }

    /// <summary>Panel user id; <see cref="Guid.Empty"/> for an anonymous caller.</summary>
    Guid UserId
    {
        get
        {
            return Caller is PrincipalCaller principal ? principal.UserId : Guid.Empty;
        }
    }

    /// <summary>The panel user's login name; empty for an anonymous caller, never null.</summary>
    string Username
    {
        get
        {
            return Caller is PrincipalCaller principal ? principal.Username : string.Empty;
        }
    }

    /// <summary>Owning account of a customer; null for an administrator and for an anonymous caller.</summary>
    Guid? AccountId
    {
        get
        {
            return Caller is CustomerCaller customer ? customer.AccountId : null;
        }
    }

    /// <summary>True for server administrators.</summary>
    bool IsAdmin
    {
        get
        {
            return Caller is AdministratorCaller;
        }
    }

    /// <summary>The operator viewing as the login, or null when nobody is.</summary>
    Guid? ActorUserId
    {
        get
        {
            return Caller is PrincipalCaller principal ? principal.ViewedBy?.UserId : null;
        }
    }

    /// <summary>The login name of <see cref="ActorUserId"/>; empty whenever that is null, never null itself.</summary>
    string ActorUsername
    {
        get
        {
            return Caller is PrincipalCaller principal ? principal.ViewedBy?.Username ?? string.Empty : string.Empty;
        }
    }
}
