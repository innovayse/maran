using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Host.Security;

/// <summary>
/// The authenticated principal, read from the current request's validated access token.
/// </summary>
/// <remarks>
/// Every answer degrades to the least privileged one when the claim is absent or unreadable: no
/// user, no account, not an administrator. That is deliberate and carried over from the stub this
/// replaced — code that starts checking permissions must deny by default, so the failure mode of a
/// missing claim is a refusal rather than a silent grant.
/// </remarks>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    /// <summary>Provides access to the ambient <see cref="HttpContext"/>, when one exists.</summary>
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the accessor.</summary>
    /// <param name="httpContextAccessor">Provides access to the ambient <see cref="HttpContext"/>.</param>
    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Guid UserId
    {
        get
        {
            return Guid.TryParse(Claim(PanelClaimTypes.UserId), out var id) ? id : Guid.Empty;
        }
    }

    /// <inheritdoc />
    public Guid? AccountId
    {
        get
        {
            return Guid.TryParse(Claim(PanelClaimTypes.AccountId), out var id) ? id : null;
        }
    }

    /// <inheritdoc />
    public bool IsAdmin
    {
        get
        {
            // An exact match against the one role name that grants everything. A claim carrying an
            // unknown role — from an older token, or a forged one that still verified — is not an
            // administrator, which is the answer that fails closed.
            return string.Equals(Claim(PanelClaimTypes.Role), nameof(UserRole.Admin), StringComparison.Ordinal);
        }
    }

    /// <summary>Reads one claim of the current request's principal.</summary>
    /// <param name="type">The claim name, from <see cref="PanelClaimTypes"/>.</param>
    /// <returns>The claim's value, or null outside a request and for an unauthenticated one.</returns>
    private string? Claim(string type)
    {
        return _httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;
    }
}
