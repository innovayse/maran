using Maran.Modules.Monitoring.Commands.ChangeLogLevel;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Tests.Commands.ChangeLogLevel;

/// <summary>What the log level change accepts and what it refuses.</summary>
public sealed class ChangeLogLevelCommandValidatorTests
{
    /// <summary>Every level the panel offers is accepted.</summary>
    [Fact]
    public void Every_level_the_panel_offers_is_accepted()
    {
        var validator = new ChangeLogLevelCommandValidator();

        foreach (var level in Enum.GetValues<PanelLogLevel>())
        {
            Assert.True(validator.Validate(new ChangeLogLevelCommand(level)).IsValid, level.ToString());
        }
    }

    /// <summary>A number that names no level is refused with the module's code.</summary>
    [Fact]
    public void A_number_that_names_no_level_is_refused_with_the_modules_code()
    {
        var result = new ChangeLogLevelCommandValidator().Validate(new ChangeLogLevelCommand((PanelLogLevel)99));

        var failure = Assert.Single(result.Errors);
        Assert.Equal("LogLevelUnknown", failure.ErrorCode);
    }
}
