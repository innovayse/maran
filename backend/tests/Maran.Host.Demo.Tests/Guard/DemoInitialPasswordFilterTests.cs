using System.Net;
using Maran.Host.Demo.Guard;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>An account creation that asks for a login with a visitor-chosen password is refused; an invitation is not.</summary>
public sealed class DemoInitialPasswordFilterTests
{
    /// <summary>A creation asking for the login mode given.</summary>
    /// <param name="mode">How the owner's login is established.</param>
    /// <returns>The command.</returns>
    private static CreateAccountCommand Command(OwnerLoginMode mode)
    {
        return new CreateAccountCommand(
            "acme",
            "acme.example",
            Guid.NewGuid(),
            "owner@acme.example",
            LoginMode: mode,
            InitialPassword: mode == OwnerLoginMode.InitialPassword ? new SensitiveString("a-long-password-1") : null);
    }

    /// <summary>Runs the filter over an action whose bound arguments are given.</summary>
    /// <param name="arguments">The bound arguments.</param>
    /// <returns>Whether the action ran, and the response status the filter wrote.</returns>
    private static async Task<(bool ActionRan, int Status)> RunAsync(params object[] arguments)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            arguments.ToDictionary(argument => { return Guid.NewGuid().ToString(); }, argument => { return (object?)argument; }),
            controller: new object());
        var ran = false;

        await new DemoInitialPasswordFilter().OnActionExecutionAsync(
            context,
            () =>
            {
                ran = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            });

        return (ran, http.Response.StatusCode);
    }

    /// <summary>The refusing control: a creation with a chosen password is answered 403 and never runs.</summary>
    [Fact]
    public async Task A_creation_with_a_chosen_password_is_refused()
    {
        var (ran, status) = await RunAsync(Command(OwnerLoginMode.InitialPassword));

        Assert.False(ran);
        Assert.Equal((int)HttpStatusCode.Forbidden, status);
    }

    /// <summary>The accepting control: a creation by invitation runs.</summary>
    [Fact]
    public async Task A_creation_by_invitation_runs()
    {
        var (ran, _) = await RunAsync(Command(OwnerLoginMode.Invitation));

        Assert.True(ran);
    }

    /// <summary>An action with no such command is not judged.</summary>
    [Fact]
    public async Task An_action_without_the_command_runs()
    {
        var (ran, _) = await RunAsync("some other argument");

        Assert.True(ran);
    }
}
