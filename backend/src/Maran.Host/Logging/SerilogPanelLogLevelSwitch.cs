using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Serilog.Core;
using Serilog.Events;

namespace Maran.Host.Logging;

/// <summary>
/// The panel's <see cref="IPanelLogLevelSwitch"/>: a window onto the Serilog
/// <see cref="LoggingLevelSwitch"/> the host's logger is built with (issue #131).
/// </summary>
/// <remarks>
/// The logger reads the switch on every event, so a change made here is in force for the next line
/// written, with no restart and no rebuild of the logger. The two enums map one to one in both
/// directions — <see cref="PanelLogLevel"/> was given six members for exactly that — so
/// <see cref="Current"/> never has to round a level it cannot name.
/// </remarks>
public sealed class SerilogPanelLogLevelSwitch : IPanelLogLevelSwitch
{
    /// <summary>The switch the host's logger was built with.</summary>
    private readonly LoggingLevelSwitch _levelSwitch;

    /// <summary>Creates the window over the logger's switch.</summary>
    /// <param name="levelSwitch">The switch passed to <c>MinimumLevel.ControlledBy</c>.</param>
    public SerilogPanelLogLevelSwitch(LoggingLevelSwitch levelSwitch)
    {
        _levelSwitch = levelSwitch;
    }

    /// <inheritdoc />
    public PanelLogLevel Current
    {
        get
        {
            return ToPanelLevel(_levelSwitch.MinimumLevel);
        }
    }

    /// <inheritdoc />
    public void Apply(PanelLogLevel level)
    {
        _levelSwitch.MinimumLevel = ToSerilogLevel(level);
    }

    /// <summary>Translates the logger's level into the panel's name for it.</summary>
    /// <param name="level">The logger's level.</param>
    /// <returns>The same level as the panel names it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a Serilog level.</exception>
    private static PanelLogLevel ToPanelLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => PanelLogLevel.Verbose,
            LogEventLevel.Debug => PanelLogLevel.Debug,
            LogEventLevel.Information => PanelLogLevel.Information,
            LogEventLevel.Warning => PanelLogLevel.Warning,
            LogEventLevel.Error => PanelLogLevel.Error,
            LogEventLevel.Fatal => PanelLogLevel.Fatal,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Not a Serilog level."),
        };
    }

    /// <summary>Translates the panel's level into the logger's.</summary>
    /// <param name="level">The panel's level.</param>
    /// <returns>The same level as the logger names it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value names no member. The module's validator refuses such a value before it gets here, so
    /// reaching this arm is a bug, and a bug is what an exception is for (rules/csharp.md).
    /// </exception>
    private static LogEventLevel ToSerilogLevel(PanelLogLevel level)
    {
        return level switch
        {
            PanelLogLevel.Verbose => LogEventLevel.Verbose,
            PanelLogLevel.Debug => LogEventLevel.Debug,
            PanelLogLevel.Information => LogEventLevel.Information,
            PanelLogLevel.Warning => LogEventLevel.Warning,
            PanelLogLevel.Error => LogEventLevel.Error,
            PanelLogLevel.Fatal => LogEventLevel.Fatal,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Not a panel log level."),
        };
    }
}
