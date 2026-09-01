using FluentValidation.TestHelper;
using Maran.Modules.Sites.Commands.ChangeSitePhpVersion;

namespace Maran.Modules.Sites.Tests.Commands.ChangeSitePhpVersion;

/// <summary>Field rules of <see cref="ChangeSitePhpVersionCommandValidator"/>.</summary>
public sealed class ChangeSitePhpVersionCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ChangeSitePhpVersionCommandValidator _validator = new();

    /// <summary>A two component version passes.</summary>
    [Fact]
    public void A_two_component_version_passes()
    {
        _validator.TestValidate(Command("8.4")).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A malformed version is rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("8")]
    [InlineData("8.3.1")]
    [InlineData("latest")]
    [InlineData("8.3\n")]
    public void A_malformed_version_is_rejected(string version)
    {
        // The trailing-newline case is the one a `$`-anchored pattern would accept: the version
        // names a php-fpm pool directory in a rendered config.
        _validator.TestValidate(Command(version))
            .ShouldHaveValidationErrorFor(command => command.PhpVersion);
    }

    /// <summary>A rejected version reports a resource key rather than an english sentence.</summary>
    [Fact]
    public void A_rejected_version_reports_a_resource_key_rather_than_an_english_sentence()
    {
        var result = _validator.TestValidate(Command("latest"));

        var message = result.Errors[0].ErrorMessage;
        Assert.Equal("PhpVersionInvalidFormat", message);
        Assert.True(message.All(char.IsLetterOrDigit));
    }

    /// <summary>A command with no site is rejected.</summary>
    [Fact]
    public void A_command_with_no_site_is_rejected()
    {
        _validator.TestValidate(new ChangeSitePhpVersionCommand(Guid.Empty, "8.3", "198.51.100.7", "tests"))
            .ShouldHaveValidationErrorFor(command => command.SiteId);
    }

    /// <summary>Builds the command under test.</summary>
    /// <param name="version">The version to validate.</param>
    private static ChangeSitePhpVersionCommand Command(string version)
    {
        return new ChangeSitePhpVersionCommand(Guid.NewGuid(), version, "198.51.100.7", "tests");
    }
}
