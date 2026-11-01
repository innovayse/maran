using System.Globalization;
using Maran.Host.Logging;
using Maran.Sdk.Contracts;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maran.Host.Tests.Logging;

/// <summary>The panel's log level switch drives the logger it was built with, at runtime.</summary>
public sealed class SerilogPanelLogLevelSwitchTests
{
    /// <summary>Every panel level round-trips through the logger's switch unchanged.</summary>
    [Fact]
    public void Every_panel_level_round_trips_through_the_loggers_switch_unchanged()
    {
        var panelSwitch = new SerilogPanelLogLevelSwitch(new LoggingLevelSwitch());

        foreach (var level in Enum.GetValues<PanelLogLevel>())
        {
            panelSwitch.Apply(level);
            Assert.Equal(level, panelSwitch.Current);
        }
    }

    /// <summary>A level applied after the logger was built changes what it writes, without a rebuild.</summary>
    [Fact]
    public void A_level_applied_after_the_logger_was_built_changes_what_it_writes_without_a_rebuild()
    {
        var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Sink(sink)
            .CreateLogger();
        var panelSwitch = new SerilogPanelLogLevelSwitch(levelSwitch);

        logger.Debug("before");
        panelSwitch.Apply(PanelLogLevel.Debug);
        logger.Debug("after");
        panelSwitch.Apply(PanelLogLevel.Error);
        logger.Warning("silenced");

        Assert.Equal(["after"], sink.Messages);
    }

    /// <summary>A value that names no level is a bug and is refused loudly.</summary>
    [Fact]
    public void A_value_that_names_no_level_is_a_bug_and_is_refused_loudly()
    {
        var panelSwitch = new SerilogPanelLogLevelSwitch(new LoggingLevelSwitch());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            panelSwitch.Apply((PanelLogLevel)99);
        });
    }

    /// <summary>Collects the rendered text of every event it receives.</summary>
    private sealed class CollectingSink : ILogEventSink
    {
        /// <summary>Every message received, in order.</summary>
        public List<string> Messages { get; } = [];

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            Messages.Add(logEvent.RenderMessage(CultureInfo.InvariantCulture));
        }
    }
}
