using FluentValidation.TestHelper;
using Maran.Modules.Sites.Commands.SetPhpExtension;
using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Commands.SetPhpExtension;

/// <summary>Field rules of <see cref="SetPhpExtensionCommandValidator"/>.</summary>
public sealed class SetPhpExtensionCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly SetPhpExtensionCommandValidator _validator = new();

    /// <summary>A two component version and every catalogue name pass.</summary>
    [Fact]
    public void A_version_and_every_catalogue_name_pass()
    {
        Assert.NotEmpty(PhpExtensionCatalogue.Names);
        Assert.All(PhpExtensionCatalogue.Names, name =>
        {
            _validator.TestValidate(Command("8.3", name)).ShouldNotHaveAnyValidationErrors();
        });
    }

    /// <summary>A name outside the catalogue is refused, whatever it looks like.</summary>
    /// <param name="name">A candidate outside the catalogue.</param>
    [Theory]
    [InlineData("")]
    [InlineData("xdebug")]
    [InlineData("GD")]
    [InlineData("php8.3-gd")]
    [InlineData("gd;rm -rf /")]
    [InlineData("gd\n")]
    [InlineData("../../etc/passwd")]
    public void An_extension_outside_the_catalogue_is_refused(string name)
    {
        _validator.TestValidate(Command("8.3", name))
            .ShouldHaveValidationErrorFor(command => command.Extension)
            .WithErrorMessage("PhpExtensionNotAllowed");
    }

    /// <summary>A malformed version is refused with the version format key.</summary>
    /// <param name="version">A candidate that is not a two component version.</param>
    [Theory]
    [InlineData("")]
    [InlineData("8")]
    [InlineData("8.3.1")]
    [InlineData("latest")]
    [InlineData("8.3\n")]
    [InlineData("../8.3")]
    public void A_malformed_version_is_refused(string version)
    {
        // The trailing-newline case is the one a `$`-anchored pattern would accept: the version names
        // a php-fpm pool directory in a rendered config.
        _validator.TestValidate(Command(version, "gd"))
            .ShouldHaveValidationErrorFor(command => command.Version)
            .WithErrorMessage("PhpVersionInvalidFormat");
    }

    /// <summary>A refusal reports a resource key rather than an English sentence.</summary>
    [Fact]
    public void A_refusal_reports_a_resource_key_rather_than_an_english_sentence()
    {
        var result = _validator.TestValidate(Command("latest", "xdebug"));

        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error =>
        {
            Assert.True(error.ErrorMessage.All(char.IsLetterOrDigit), error.ErrorMessage);
        });
    }

    /// <summary>Builds the command under test.</summary>
    /// <param name="version">The version to validate.</param>
    /// <param name="extension">The extension name to validate.</param>
    private static SetPhpExtensionCommand Command(string version, string extension)
    {
        return new SetPhpExtensionCommand(true, version, extension, "198.51.100.7", "tests");
    }
}
