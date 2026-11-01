using System.Globalization;
using Maran.Modules.Monitoring.Queries.GetLogLevel;
using Maran.Modules.Monitoring.Resources;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Monitoring.Tests.Queries.GetLogLevel;

/// <summary>Reading the panel's log level: the running level, and every choice by name.</summary>
public sealed class GetLogLevelQueryHandlerTests
{
    /// <summary>The level reported is the running logger's, not a stored one.</summary>
    [Fact]
    public void The_level_reported_is_the_running_loggers()
    {
        var handler = new GetLogLevelQueryHandler(new FakePanelLogLevelSwitch(PanelLogLevel.Error), Localizer());

        var result = handler.Handle(new GetLogLevelQuery());

        Assert.Equal(PanelLogLevel.Error, result.Value!.Level);
    }

    /// <summary>Every level is offered, each with a name of its own rather than its key.</summary>
    [Fact]
    public void Every_level_is_offered_each_with_a_name_of_its_own_rather_than_its_key()
    {
        var handler = new GetLogLevelQueryHandler(new FakePanelLogLevelSwitch(), Localizer());

        var choices = handler.Handle(new GetLogLevelQuery()).Value!.Choices;

        Assert.Equal(Enum.GetValues<PanelLogLevel>(), choices.Select(choice =>
        {
            return choice.Level;
        }));
        Assert.All(choices, choice =>
        {
            Assert.DoesNotContain("LogLevel", choice.LevelDisplayName, StringComparison.Ordinal);
        });
    }

    /// <summary>The names follow the caller's language.</summary>
    [Fact]
    public void The_names_follow_the_callers_language()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
            var handler = new GetLogLevelQueryHandler(new FakePanelLogLevelSwitch(), Localizer());

            var debug = handler.Handle(new GetLogLevelQuery()).Value!.Choices
                .Single(choice =>
                {
                    return choice.Level == PanelLogLevel.Debug;
                });

            Assert.StartsWith("Отладка", debug.LevelDisplayName, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>Builds the resx-backed localizer the module uses in production.</summary>
    /// <returns>The display-name localizer.</returns>
    private static StringLocalizer<DisplayNames> Localizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new StringLocalizer<DisplayNames>(factory);
    }
}
