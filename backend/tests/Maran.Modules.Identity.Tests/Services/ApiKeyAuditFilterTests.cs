using System.Net;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// The per-call journal of API key calls: one entry for a key, none for anybody else, and never the
/// concrete URL, the query string or a header.
/// </summary>
public sealed class ApiKeyAuditFilterTests
{
    private const string Template = "api/v1/accounts/{id}";

    private readonly RecordingAuditWriter _audit = new();

    /// <summary>A call made with a key writes one entry naming the key, method, template, status and address.</summary>
    [Fact]
    public async Task A_key_call_writes_one_entry_naming_the_key_method_template_status_and_address()
    {
        var keyId = Guid.NewGuid();
        var caller = new ApiKeyCaller(keyId, ["self.read"]);

        await RunAsync(caller, "GET", "/api/v1/accounts/5", string.Empty, new OkResult());

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ApiKeyCall, entry.Action);
        Assert.Equal(caller.ActorName, entry.ActorUsername);
        Assert.Contains(keyId.ToString("N"), entry.ActorUsername, StringComparison.Ordinal);
        Assert.Null(entry.ActorUserId);
        Assert.Equal("GET " + Template + " 200", entry.Subject);
        Assert.Equal("203.0.113.9", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An administrator call writes nothing: the filter acts for keys only (inverse control).</summary>
    [Fact]
    public async Task An_administrator_call_writes_nothing()
    {
        var administrator = new AdministratorCaller(Guid.NewGuid(), "admin");

        await RunAsync(administrator, "GET", "/api/v1/accounts/5", string.Empty, new OkResult());

        Assert.Empty(_audit.Written);
    }

    /// <summary>An anonymous call writes nothing (inverse control).</summary>
    [Fact]
    public async Task An_anonymous_call_writes_nothing()
    {
        await RunAsync(AnonymousCaller.Instance, "GET", "/api/v1/modules", string.Empty, new OkResult());

        Assert.Empty(_audit.Written);
    }

    /// <summary>The entry carries the route template and never the query string or the concrete path.</summary>
    [Fact]
    public async Task The_entry_never_contains_the_query_string_or_the_concrete_path()
    {
        var caller = new ApiKeyCaller(Guid.NewGuid(), ["self.read"]);

        await RunAsync(caller, "GET", "/api/v1/accounts/7731", "?search=secret-needle&apikey=mk_leak", new OkResult());

        var entry = Assert.Single(_audit.Written);
        Assert.DoesNotContain("secret-needle", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("mk_leak", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("7731", entry.Subject, StringComparison.Ordinal);
        Assert.Equal(string.Empty, entry.UserAgent);
    }

    /// <summary>An action that throws is journalled as a 500 and the entry reads as failed.</summary>
    [Fact]
    public async Task An_action_that_throws_is_journalled_as_a_500()
    {
        var caller = new ApiKeyCaller(Guid.NewGuid(), ["self.read"]);

        await RunAsync(caller, "POST", "/api/v1/accounts", string.Empty, null, new InvalidOperationException());

        var entry = Assert.Single(_audit.Written);
        Assert.EndsWith(" 500", entry.Subject, StringComparison.Ordinal);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The status of the action's own result is what is recorded.</summary>
    [Fact]
    public async Task The_status_of_the_actions_own_result_is_recorded()
    {
        var caller = new ApiKeyCaller(Guid.NewGuid(), ["self.read"]);

        await RunAsync(caller, "GET", "/api/v1/accounts/5", string.Empty, new NotFoundResult());

        var entry = Assert.Single(_audit.Written);
        Assert.EndsWith(" 404", entry.Subject, StringComparison.Ordinal);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A failing journal never changes the answer: the filter swallows and logs the write failure.</summary>
    [Fact]
    public async Task A_failing_journal_does_not_change_the_answer()
    {
        var caller = new ApiKeyCaller(Guid.NewGuid(), ["self.read"]);
        var filter = new ApiKeyAuditFilter(
            new CallerUser(caller),
            new IdentityAuditJournal(new ThrowingAuditWriter(), new CallerUser(caller)),
            NullLogger<ApiKeyAuditFilter>.Instance);

        var exception = await Record.ExceptionAsync(
            () => InvokeAsync(filter, "GET", "/api/v1/accounts/5", string.Empty, new OkResult(), null));

        Assert.Null(exception);
    }

    /// <summary>Runs the filter for a caller over a fake action and its result.</summary>
    /// <param name="caller">Who the request runs as.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The concrete request path.</param>
    /// <param name="query">The query string, with its leading question mark, or empty.</param>
    /// <param name="result">The action's result, or <c>null</c> when it throws.</param>
    /// <param name="thrown">The exception the action raised, if any.</param>
    /// <returns>Resolves once the filter has run.</returns>
    private async Task RunAsync(
        Caller caller,
        string method,
        string path,
        string query,
        IActionResult? result,
        Exception? thrown = null)
    {
        var user = new CallerUser(caller);
        var filter = new ApiKeyAuditFilter(
            user,
            new IdentityAuditJournal(_audit, user),
            NullLogger<ApiKeyAuditFilter>.Instance);

        await InvokeAsync(filter, method, path, query, result, thrown);
    }

    /// <summary>Drives a filter through one request against the fake action.</summary>
    /// <param name="filter">The filter under test.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The concrete request path.</param>
    /// <param name="query">The query string, or empty.</param>
    /// <param name="result">The action's result, or <c>null</c> when it throws.</param>
    /// <param name="thrown">The exception the action raised, if any.</param>
    /// <returns>Resolves once the filter has run.</returns>
    private static async Task InvokeAsync(
        ApiKeyAuditFilter filter,
        string method,
        string path,
        string query,
        IActionResult? result,
        Exception? thrown)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");

        var descriptor = new ControllerActionDescriptor
        {
            AttributeRouteInfo = new AttributeRouteInfo { Template = Template },
        };
        var actionContext = new ActionContext(http, new RouteData(), descriptor);
        var executing = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());

        await filter.OnActionExecutionAsync(
            executing,
            () =>
            {
                var executed = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object())
                {
                    Result = result,
                    Exception = thrown,
                };
                return Task.FromResult(executed);
            });
    }

    /// <summary>An <see cref="ICurrentUser"/> double speaking for one given caller.</summary>
    private sealed class CallerUser : ICurrentUser
    {
        /// <inheritdoc />
        public Caller Caller { get; }

        /// <summary>Creates the double.</summary>
        /// <param name="caller">Who the request runs as.</param>
        public CallerUser(Caller caller)
        {
            Caller = caller;
        }
    }

    /// <summary>An <see cref="IAuditWriter"/> whose every write fails.</summary>
    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        /// <inheritdoc />
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The journal is down.");
        }
    }
}
