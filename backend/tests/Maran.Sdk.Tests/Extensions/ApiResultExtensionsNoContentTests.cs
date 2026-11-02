using Maran.Sdk.Extensions;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Extensions;

/// <summary>
/// Behavioural contract of the value-less <see cref="Result"/> translation: success answers 204 with
/// no body, and failure is the very same problem response the generic path writes.
/// </summary>
public sealed class ApiResultExtensionsNoContentTests
{
    /// <summary>Every kind of failure, as xUnit theory rows.</summary>
    /// <returns>One row per kind.</returns>
    public static IEnumerable<object[]> Kinds()
    {
        return Enum.GetValues<ErrorType>().Select(kind => { return new object[] { kind }; });
    }

    /// <summary>A successful value-less result answers 204 No Content and carries no body.</summary>
    [Fact]
    public void A_successful_valueless_result_answers_no_content_with_no_body()
    {
        var response = Result.Ok().ToActionResult(NewHttpContext());

        var noContent = Assert.IsType<NoContentResult>(response);
        Assert.Equal(StatusCodes.Status204NoContent, noContent.StatusCode);
    }

    /// <summary>A failed value-less result answers the problem response for its code and kind.</summary>
    [Fact]
    public void A_failed_valueless_result_answers_a_problem_carrying_its_code()
    {
        var response = Result.Fail(Error.Of("SiteNotFound", ErrorType.NotFound)).ToActionResult(NewHttpContext());

        var objectResult = Assert.IsType<ObjectResult>(response);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.Equal("SiteNotFound", problem.Extensions["code"]);
    }

    /// <summary>For every kind of failure the value-less and the generic translation answer identically.</summary>
    /// <param name="type">The kind of failure carried by both results.</param>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_failure_translates_identically_with_and_without_a_value(ErrorType type)
    {
        var error = Error.Of("MaranProbeCode", type);
        var context = NewHttpContext();
        context.Items[CorrelationIdKeys.ItemsKey] = "corr-1";

        var valueless = Assert.IsType<ObjectResult>(Result.Fail(error).ToActionResult(context));
        var generic = Assert.IsType<ObjectResult>(Result<string>.Fail(error).ToActionResult(context));

        var valuelessProblem = Assert.IsType<ProblemDetails>(valueless.Value);
        var genericProblem = Assert.IsType<ProblemDetails>(generic.Value);
        Assert.Equal(generic.StatusCode, valueless.StatusCode);
        Assert.Equal(genericProblem.Status, valuelessProblem.Status);
        Assert.Equal(genericProblem.Title, valuelessProblem.Title);
        Assert.Equal(genericProblem.Detail, valuelessProblem.Detail);
        Assert.Equal(genericProblem.Type, valuelessProblem.Type);
        Assert.Equal(genericProblem.Extensions["code"], valuelessProblem.Extensions["code"]);
        Assert.Equal("corr-1", valuelessProblem.Extensions["correlationId"]);
    }

    /// <summary>Builds a request context with an empty container, the way an unloaded host presents one.</summary>
    /// <returns>The context handed to the extension under test.</returns>
    private static DefaultHttpContext NewHttpContext()
    {
        return new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
    }
}
