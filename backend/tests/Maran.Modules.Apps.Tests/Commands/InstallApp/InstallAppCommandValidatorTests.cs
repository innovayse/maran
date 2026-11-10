using Maran.Modules.Apps.Commands.InstallApp;

namespace Maran.Modules.Apps.Tests.Commands.InstallApp;

/// <summary>The boundary: only the catalogue's spellings reach a task or the agent.</summary>
public sealed class InstallAppCommandValidatorTests
{
    /// <summary>A member of the catalogue is accepted.</summary>
    [Fact]
    public void A_catalogue_id_is_accepted()
    {
        Assert.True(new InstallAppCommandValidator().Validate(new InstallAppCommand("php-8.3")).IsValid);
    }

    /// <summary>A package name, a path and an empty id are refused with the code.</summary>
    /// <param name="text">The candidate.</param>
    [Theory]
    [InlineData("apache2")]
    [InlineData("../../etc/passwd")]
    [InlineData("")]
    public void Anything_else_is_refused(string text)
    {
        var result = new InstallAppCommandValidator().Validate(new InstallAppCommand(text));

        Assert.False(result.IsValid);
        Assert.Equal("AppIdInvalid", Assert.Single(result.Errors).ErrorCode);
    }
}
