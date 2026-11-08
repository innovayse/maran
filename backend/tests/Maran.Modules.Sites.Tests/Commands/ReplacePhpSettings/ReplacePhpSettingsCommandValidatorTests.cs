using Maran.Modules.Sites.Commands.ReplacePhpSettings;
using Maran.Modules.Sites.Common;

namespace Maran.Modules.Sites.Tests.Commands.ReplacePhpSettings;

/// <summary>Behavioral contract of <see cref="ReplacePhpSettingsCommandValidator"/>.</summary>
public sealed class ReplacePhpSettingsCommandValidatorTests
{
    /// <summary>A well-formed list passes.</summary>
    [Fact]
    public void A_well_formed_list_passes()
    {
        var result = Validate(
            ("memory_limit", "256"),
            ("upload_max_filesize", "32"),
            ("post_max_size", "64"),
            ("max_execution_time", "60"),
            ("max_input_vars", "2000"),
            ("date.timezone", "Europe/Berlin"));

        Assert.True(result.IsValid);
    }

    /// <summary>An empty list passes: it removes every setting.</summary>
    [Fact]
    public void An_empty_list_passes()
    {
        Assert.True(Validate().IsValid);
    }

    /// <summary>A request that leaves the list out is refused.</summary>
    [Fact]
    public void A_request_that_leaves_the_list_out_is_refused()
    {
        var result = new ReplacePhpSettingsCommandValidator().Validate(new ReplacePhpSettingsCommand(null!));

        Assert.Equal("PhpSettingsRequired", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A setting the pool protects, or any unknown name, is refused.</summary>
    [Theory]
    [InlineData("open_basedir")]
    [InlineData("disable_functions")]
    [InlineData("cgi.fix_pathinfo")]
    [InlineData("memory_limit\nphp_admin_value[x]")]
    [InlineData("")]
    public void A_setting_the_pool_protects_is_refused(string name)
    {
        var result = Validate((name, "1"));

        Assert.Equal("PhpSettingNotAllowed", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>The same setting listed twice is refused.</summary>
    [Fact]
    public void The_same_setting_listed_twice_is_refused()
    {
        var result = Validate(("memory_limit", "128"), ("memory_limit", "256"));

        Assert.Equal("PhpSettingDuplicated", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A value of the wrong shape or beyond the agent's ceiling is refused.</summary>
    [Theory]
    [InlineData("memory_limit", "")]
    [InlineData("memory_limit", "0")]
    [InlineData("memory_limit", "256M")]
    [InlineData("memory_limit", "-1")]
    [InlineData("memory_limit", "513")]
    [InlineData("max_execution_time", "301")]
    [InlineData("max_input_vars", "10001")]
    [InlineData("date.timezone", "../../etc/passwd")]
    [InlineData("date.timezone", "UTC\nphp_value[x]=y")]
    public void A_value_of_the_wrong_shape_or_beyond_the_agents_ceiling_is_refused(string name, string value)
    {
        var result = Validate((name, value));

        Assert.Equal("PhpSettingValueInvalid", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>Stating the upload size without the request size is refused.</summary>
    [Fact]
    public void Stating_the_upload_size_without_the_request_size_is_refused()
    {
        var result = Validate(("upload_max_filesize", "32"));

        Assert.Equal("PhpSettingSizesTogether", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>Stating the request size without the upload size is refused.</summary>
    [Fact]
    public void Stating_the_request_size_without_the_upload_size_is_refused()
    {
        var result = Validate(("post_max_size", "32"));

        Assert.Equal("PhpSettingSizesTogether", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A request size below the upload size is refused.</summary>
    [Fact]
    public void A_request_size_below_the_upload_size_is_refused()
    {
        var result = Validate(("upload_max_filesize", "64"), ("post_max_size", "32"));

        Assert.Equal("PhpSettingPostSizeBelowUpload", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A request size equal to the upload size is allowed.</summary>
    [Fact]
    public void A_request_size_equal_to_the_upload_size_is_allowed()
    {
        Assert.True(Validate(("upload_max_filesize", "32"), ("post_max_size", "32")).IsValid);
    }

    /// <summary>Validates a list built from name and value pairs.</summary>
    /// <param name="settings">The pairs to send.</param>
    private static FluentValidation.Results.ValidationResult Validate(params (string Name, string Value)[] settings)
    {
        var command = new ReplacePhpSettingsCommand(
            settings.Select(pair => { return new PhpSettingInputDto(pair.Name, pair.Value); }).ToList());

        return new ReplacePhpSettingsCommandValidator().Validate(command);
    }
}
