using Maran.Sdk.Extensions;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Sdk.Controllers;

/// <summary>
/// The base every module controller inherits — never <see cref="ControllerBase"/> directly
/// (rules/csharp.md "Cross-cutting infrastructure"). Fixes the route convention, exposes the
/// authenticated caller and the request's correlation id, and gives derived controllers the
/// <see cref="Result{T}"/>-to-<see cref="IActionResult"/> translation via <see cref="ApiResultExtensions"/>
/// so no module re-implements it.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>The authenticated principal of the current request.</summary>
    protected ICurrentUser CurrentUser { get; }

    /// <summary>Creates the controller with the caller identity every derived controller needs.</summary>
    /// <param name="currentUser">
    /// The authenticated principal of the current request. No implementation is registered until
    /// authentication ships (Plan 2); no module controller exists yet to exercise this either.
    /// </param>
    protected BaseApiController(ICurrentUser currentUser)
    {
        CurrentUser = currentUser;
    }

    /// <summary>The correlation id assigned to this request by <c>CorrelationIdMiddleware</c>, or <c>null</c> outside a request.</summary>
    protected string? CorrelationId
    {
        get
        {
            return HttpContext.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var value) ? value as string : null;
        }
    }

    /// <summary>
    /// The caller's address, read from the connection and never from a header a caller controls,
    /// in the panel's canonical spelling — or <see cref="ClientAddress.Unknown"/> when the
    /// connection reports no peer.
    /// </summary>
    /// <remarks>
    /// Here for the same reason <see cref="CurrentUser"/> and <see cref="CorrelationId"/> are: every
    /// module controller that writes an audit command needs it, and eleven of them had written the
    /// expression out privately — each copy dropping the IPv4-mapped normalisation that
    /// <see cref="ClientAddress"/> exists to perform, so a ban built from one matched no packet that
    /// ever arrived. The proxy-forwarded address has already replaced the peer by the time a
    /// controller runs; `ForwardedHeaders` is trusted only from the local reverse proxy.
    /// </remarks>
    protected string ClientIpAddress
    {
        get
        {
            return ClientAddress.Of(HttpContext.Connection.RemoteIpAddress);
        }
    }

    /// <summary>
    /// The caller's <c>User-Agent</c>, capped to the length the panel stores.
    /// </summary>
    /// <remarks>
    /// Here for the same reason <see cref="ClientIpAddress"/> is. Four Identity controllers had
    /// written this out privately, each repeating the length as a literal that also appears in two
    /// EF configurations and their columns — five places obliged to agree with nothing relating
    /// them. The cap itself lives in <see cref="UserAgentText"/>, which the configurations read, so
    /// widening the column widens the truncation.
    /// </remarks>
    protected string CallerUserAgent
    {
        get
        {
            return UserAgentText.Capped(Request.Headers.UserAgent.ToString());
        }
    }

    /// <summary>Translates a query/read result into 200 OK or a problem response.</summary>
    /// <param name="result">The outcome to translate.</param>
    protected IActionResult ToActionResult<T>(Result<T> result)
    {
        return result.ToActionResult(HttpContext);
    }

    /// <summary>Translates a create result into 201 Created (with <paramref name="location"/>) or a problem response.</summary>
    /// <param name="result">The outcome to translate.</param>
    /// <param name="location">The URI of the created resource, used as the 201 response's <c>Location</c> header.</param>
    protected IActionResult ToCreatedActionResult<T>(Result<T> result, string location)
    {
        return result.ToCreatedActionResult(HttpContext, location);
    }
}
