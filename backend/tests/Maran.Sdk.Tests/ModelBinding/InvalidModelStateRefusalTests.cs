using FluentValidation;
using Maran.Sdk.ModelBinding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace Maran.Sdk.Tests.ModelBinding;

/// <summary>Behavioural contract of <see cref="InvalidModelStateRefusal"/>.</summary>
public sealed class InvalidModelStateRefusalTests
{
    /// <summary>
    /// A value that failed to bind is refused with <c>RequestParameterInvalid</c>, carried as both the
    /// failure's message and its code and naming the entry that failed — not the valid one bound
    /// before it.
    /// </summary>
    [Fact]
    public void An_unbindable_value_is_refused_with_the_code_naming_that_value()
    {
        var modelState = new ModelStateDictionary();
        modelState.SetModelValue("Search", "shop", "shop");
        modelState.MarkFieldValid("Search");
        modelState.AddModelError("PageSize", "The value 'x' is not valid for PageSize.");

        var refusal = Assert.Throws<ValidationException>(() =>
        {
            return InvalidModelStateRefusal.Refuse(ContextOf(modelState));
        });

        var failure = Assert.Single(refusal.Errors);
        Assert.Equal("RequestParameterInvalid", failure.ErrorCode);
        Assert.Equal("RequestParameterInvalid", failure.ErrorMessage);
        Assert.Equal("PageSize", failure.PropertyName);
    }

    /// <summary>Builds an action context over the given model state.</summary>
    /// <param name="modelState">The model state the action was bound into.</param>
    private static ActionContext ContextOf(ModelStateDictionary modelState)
    {
        return new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelState);
    }
}
