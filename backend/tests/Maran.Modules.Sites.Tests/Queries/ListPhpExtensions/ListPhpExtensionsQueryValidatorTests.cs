using FluentValidation.TestHelper;
using Maran.Modules.Sites.Queries.ListPhpExtensions;

namespace Maran.Modules.Sites.Tests.Queries.ListPhpExtensions;

/// <summary>Field rules of <see cref="ListPhpExtensionsQueryValidator"/>.</summary>
public sealed class ListPhpExtensionsQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListPhpExtensionsQueryValidator _validator = new();

    /// <summary>A two component version passes.</summary>
    [Fact]
    public void A_two_component_version_passes()
    {
        _validator.TestValidate(new ListPhpExtensionsQuery("8.4")).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A malformed version is refused with the version format key.</summary>
    /// <param name="version">A candidate that is not a two component version.</param>
    [Theory]
    [InlineData("")]
    [InlineData("8")]
    [InlineData("8.3.1")]
    [InlineData("latest")]
    [InlineData("8.3\n")]
    public void A_malformed_version_is_refused(string version)
    {
        _validator.TestValidate(new ListPhpExtensionsQuery(version))
            .ShouldHaveValidationErrorFor(query => query.Version)
            .WithErrorMessage("PhpVersionInvalidFormat");
    }
}
