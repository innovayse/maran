using System.Text.Json.Serialization;
using Maran.Sdk.Extensions;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Extensions;

/// <summary>
/// Behavioural contract of the type an ok result is serialized as: the handler's declared type, so a
/// polymorphic answer keeps the member that says which case it is.
/// </summary>
public sealed class ApiResultExtensionsDeclaredTypeTests
{
    /// <summary>A read result is serialized as the declared type, not the runtime type of its value.</summary>
    [Fact]
    public void A_read_result_is_serialized_as_the_declared_type_not_the_runtime_type_of_its_value()
    {
        var result = Result<Shape>.Ok(new Circle(2));

        var response = Assert.IsType<OkObjectResult>(result.ToActionResult(NewHttpContext()));

        Assert.Equal(typeof(Shape), response.DeclaredType);
        Assert.IsType<Circle>(response.Value);
    }

    /// <summary>A create result is serialized as the declared type, not the runtime type of its value.</summary>
    [Fact]
    public void A_create_result_is_serialized_as_the_declared_type_not_the_runtime_type_of_its_value()
    {
        var result = Result<Shape>.Ok(new Circle(2));

        var response = Assert.IsType<CreatedResult>(result.ToCreatedActionResult(NewHttpContext(), "/shapes/1"));

        Assert.Equal(typeof(Shape), response.DeclaredType);
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

    /// <summary>A polymorphic base, as a root-level union answer is.</summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Circle), "circle")]
    private abstract record Shape;

    /// <summary>One case of <see cref="Shape"/>.</summary>
    /// <param name="Radius">The radius.</param>
    private sealed record Circle(int Radius) : Shape;
}
