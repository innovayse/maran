using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// A global action filter that journals every call made with an API key (issue #144: "every call with
/// a key is journalled"), and does nothing for any other caller.
/// </summary>
/// <remarks>
/// <para>
/// <b>It acts only when <see cref="ICurrentUser.Caller"/> is an <see cref="ApiKeyCaller"/>.</b> An
/// administrator, a customer, a reseller and an anonymous caller pass through untouched, so the filter
/// can be registered for every action of every module without changing what any of them costs or sees.
/// After the action has run it writes ONE entry through <see cref="IdentityAuditJournal"/>: the key id,
/// the HTTP method, the route TEMPLATE, the status code and the caller's address. Never a header, the
/// body, the concrete URL, the query string or the key itself.
/// </para>
/// <para>
/// <b>Blind spot, stated.</b> An action filter runs only once routing, authentication and
/// authorization have let the request reach an action. A key call refused BEFORE that — a 401 because
/// the key is unknown, revoked, expired or from a disallowed address, or a 403 because the key lacks
/// the scope the endpoint needs — never reaches this filter and is not journalled per call here.
/// Authentication failures are counted by the Host's failed-attempt limiter; nothing else records a
/// scope refusal. A call that the filter does see is recorded whatever the action answered, including
/// an exception, which the exception middleware turns into a 500 and which is journalled as 500.
/// </para>
/// <para>
/// <b>A failed audit write never changes the answer.</b> The filter runs after the action, so a
/// mutation has already happened by then; refusing the response would punish the caller for a gap in
/// the journal and would leave the mutation in place. The failure is logged loudly instead, and the
/// module journal of whatever was mutated records the key as the actor on its own.
/// </para>
/// </remarks>
public sealed class ApiKeyAuditFilter : IAsyncActionFilter
{
    /// <summary>The status recorded for a call whose action threw: what the exception middleware answers.</summary>
    private const int UnhandledExceptionStatus = StatusCodes.Status500InternalServerError;

    /// <summary>The authenticated principal; the filter acts only for an API key.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The journal the entry is written through.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Where a failed audit write is reported.</summary>
    private readonly ILogger<ApiKeyAuditFilter> _logger;

    /// <summary>Creates the filter.</summary>
    /// <param name="currentUser">The authenticated principal of the current request, if any.</param>
    /// <param name="journal">The Identity journal an entry is written through.</param>
    /// <param name="logger">Receives the report of a failed audit write.</param>
    public ApiKeyAuditFilter(ICurrentUser currentUser, IdentityAuditJournal journal, ILogger<ApiKeyAuditFilter> logger)
    {
        _currentUser = currentUser;
        _journal = journal;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        if (_currentUser.Caller is not ApiKeyCaller caller)
        {
            return;
        }

        var http = context.HttpContext;
        var routeTemplate = context.ActionDescriptor.AttributeRouteInfo?.Template ?? string.Empty;

        try
        {
            await _journal.RecordApiKeyCallAsync(
                caller,
                http.Request.Method,
                routeTemplate,
                StatusOf(executed, http),
                ClientAddress.Of(http.Connection.RemoteIpAddress),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The audit entry for a call made with API key {KeyId} could not be written",
                caller.KeyId);
        }
    }

    /// <summary>Reads the status the call is answered with from the executed action.</summary>
    /// <param name="executed">The outcome of the action.</param>
    /// <param name="http">The request's HTTP context.</param>
    /// <returns>The result's own status, else the response's, else 500 for an unhandled exception.</returns>
    private static int StatusOf(ActionExecutedContext executed, HttpContext http)
    {
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            return UnhandledExceptionStatus;
        }

        if (executed.Result is IStatusCodeActionResult { StatusCode: { } status })
        {
            return status;
        }

        return http.Response.StatusCode;
    }
}
