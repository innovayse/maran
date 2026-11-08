using System.Reflection;
using Maran.Modules.Files.Controllers;
using Maran.Sdk.Contracts;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Modules.Files.Tests.Controllers;

/// <summary>
/// Pins how the browse, transfer and options controllers declare who may call them and what an operator
/// viewing as a customer may do through them: looking is allowed, copying a customer's data in or out or
/// changing it is not.
/// </summary>
public sealed class FilesTransferControllerPolicyTests
{
    /// <summary>The routes that may be looked at through a view-as session, by controller and action.</summary>
    public static TheoryData<Type, string> Looking
    {
        get
        {
            return new TheoryData<Type, string>
            {
                { typeof(FilesBrowseController), nameof(FilesBrowseController.GetEntriesAsync) },
                { typeof(FilesBrowseController), nameof(FilesBrowseController.GetInfoAsync) },
                { typeof(FilesBrowseController), nameof(FilesBrowseController.SearchAsync) },
                { typeof(FilesOptionsController), nameof(FilesOptionsController.GetAsync) },
            };
        }
    }

    /// <summary>The routes that copy data out of an account or change it, which a view-as session is refused.</summary>
    public static TheoryData<Type, string> Copying
    {
        get
        {
            return new TheoryData<Type, string>
            {
                { typeof(FilesBrowseController), nameof(FilesBrowseController.GetContentAsync) },
                { typeof(FilesTransferController), nameof(FilesTransferController.SaveContentAsync) },
                { typeof(FilesTransferController), nameof(FilesTransferController.UploadAsync) },
                { typeof(FilesTransferController), nameof(FilesTransferController.DownloadAsync) },
            };
        }
    }

    /// <summary>Each controller names the any-authenticated policy alone on its class, the feature it needs, and the files rate limit.</summary>
    /// <param name="controller">The controller type.</param>
    [Theory]
    [InlineData(typeof(FilesBrowseController))]
    [InlineData(typeof(FilesTransferController))]
    [InlineData(typeof(FilesOptionsController))]
    public void A_controller_declares_its_policy_its_feature_and_its_rate_limit(Type controller)
    {
        var policies = controller
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(attribute => { return attribute.Policy ?? string.Empty; })
            .ToArray();
        var features = controller
            .GetCustomAttributes<RequiresAccountFeatureAttribute>(inherit: true)
            .Select(attribute => { return attribute.Feature; })
            .ToArray();
        var limits = controller
            .GetCustomAttributes<EnableRateLimitingAttribute>(inherit: true)
            .Select(attribute => { return attribute.PolicyName ?? string.Empty; })
            .ToArray();

        Assert.Equal([AuthorizationPolicies.AnyAuthenticated], policies);
        Assert.Equal([AccountFeature.Files], features);
        Assert.Equal([RateLimitPolicies.Files], limits);
    }

    /// <summary>A route that only looks declares the read access of a view-as session.</summary>
    /// <param name="controller">The controller type.</param>
    /// <param name="action">The action's name.</param>
    [Theory]
    [MemberData(nameof(Looking))]
    public void A_route_that_only_looks_may_be_used_through_a_view_as_session(Type controller, string action)
    {
        Assert.Equal(ViewAsAccess.Read, ViewAsOf(controller, action));
    }

    /// <summary>A route that copies data or changes it is refused to a view-as session.</summary>
    /// <param name="controller">The controller type.</param>
    /// <param name="action">The action's name.</param>
    [Theory]
    [MemberData(nameof(Copying))]
    public void A_route_that_copies_or_changes_data_is_refused_to_a_view_as_session(Type controller, string action)
    {
        Assert.Equal(ViewAsAccess.Refused, ViewAsOf(controller, action));
    }

    /// <summary>The walk sees every action these controllers declare, so neither list above can leave one out unnoticed.</summary>
    [Fact]
    public void Every_action_of_these_controllers_is_in_one_of_the_two_lists()
    {
        var listed = Looking.Concat(Copying)
            .Select(row => { return ((Type)row[0], (string)row[1]); })
            .ToHashSet();
        var declared = new[] { typeof(FilesBrowseController), typeof(FilesTransferController), typeof(FilesOptionsController) }
            .SelectMany(type =>
            {
                return type
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(method => { return (type, method.Name); });
            })
            .ToHashSet();

        Assert.Equal(declared, listed);
    }

    /// <summary>The view-as access an action declares.</summary>
    /// <param name="controller">The controller type.</param>
    /// <param name="action">The action's name.</param>
    /// <returns>The declared access.</returns>
    private static ViewAsAccess ViewAsOf(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!;
        return method.GetCustomAttribute<ViewAsAccessAttribute>()!.Access;
    }
}
