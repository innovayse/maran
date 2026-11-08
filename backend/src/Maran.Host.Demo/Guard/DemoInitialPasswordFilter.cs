using Maran.Host.Demo.Guard.Enums;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// Refuses an account creation that asks for a login with a password the visitor chose
/// (<see cref="OwnerLoginMode.InitialPassword"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a filter and not a table row.</b> The refusal table classifies by action, and creating an
/// account is allowed; what is refused is one value of one field of its body. An action filter sees the
/// bound command, so it judges every action that takes a <see cref="CreateAccountCommand"/> (the
/// administrator's and the reseller API key's alike) without the table having to know the field.
/// </para>
/// <para>
/// The demo's logins are published; a visitor-made login with a chosen password would be a persistent
/// credential the demo promises nobody can create (threat note D6, T2). Creating the account with an
/// invitation, the default, stays allowed.
/// </para>
/// </remarks>
public sealed class DemoInitialPasswordFilter : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.ActionArguments.Values.OfType<CreateAccountCommand>()
            .Any(command => { return command.LoginMode == OwnerLoginMode.InitialPassword; }))
        {
            await DemoProblemWriter.WriteRefusedAsync(context.HttpContext, DemoRefusalArea.Credentials);
            context.Result = new EmptyResult();
            return;
        }

        await next();
    }
}
