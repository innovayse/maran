using System.Reflection;
using Maran.Modules.Backups.Controllers;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maran.Modules.Backups.Tests.Controllers;

/// <summary>Pins how the verify action declares itself: route, view-as access and the statuses it can return.</summary>
public sealed class BackupsControllerVerifyTests
{
    /// <summary>The verify action under test.</summary>
    private static MethodInfo Action
    {
        get
        {
            return typeof(BackupsController).GetMethod(nameof(BackupsController.VerifyAsync))!;
        }
    }

    /// <summary>The action is a POST on the backup's own route, never a GET a link or a prefetch could fire.</summary>
    [Fact]
    public void The_verify_action_is_a_post_on_the_backups_verify_route()
    {
        var post = Action.GetCustomAttribute<HttpPostAttribute>();

        Assert.NotNull(post);
        Assert.Equal("{id:guid}/verify", post!.Template);
    }

    /// <summary>The action is refused while an operator views the panel as a customer.</summary>
    /// <remarks>
    /// It starts work on the server and records a result that outlives the look, so it is on the
    /// refused side of the allow-list (rules/csharp.md, issue #334). A missing attribute is refused
    /// at run time and by <c>ViewAsAccessDeclarationTests</c>; this pins WHICH side it is on.
    /// </remarks>
    [Fact]
    public void The_verify_action_is_refused_while_viewing_as_a_customer()
    {
        var access = Action.GetCustomAttribute<ViewAsAccessAttribute>();

        Assert.NotNull(access);
        Assert.Equal(ViewAsAccess.Refused, access!.Access);
    }

    /// <summary>The action declares a 404 for another account's backup and a 409 for one with no artifact.</summary>
    [Fact]
    public void The_verify_action_declares_not_found_and_conflict()
    {
        var statuses = Action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute =>
            {
                return attribute.StatusCode;
            })
            .ToArray();

        Assert.Contains(StatusCodes.Status404NotFound, statuses);
        Assert.Contains(StatusCodes.Status409Conflict, statuses);
        Assert.Contains(StatusCodes.Status200OK, statuses);
    }
}
