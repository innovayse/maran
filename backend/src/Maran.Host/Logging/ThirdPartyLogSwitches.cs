using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maran.Host.Logging;

/// <summary>
/// The level switches that hold the pinned third-party namespaces at or above their floor while the
/// panel's own level moves (issue #337).
/// </summary>
/// <remarks>
/// Two mechanisms hold one line, and each answers a different question. A
/// <c>MinimumLevel.Override</c> per namespace, controlled by that namespace's switch, is how the
/// logger decides cheaply, before an event is built. The filter is what makes the pin a guarantee: a
/// configuration file or environment variable can add a longer override (<c>Npgsql.Command</c>)
/// that beats the shorter namespace's, and an override cannot be told to lose to a floor, so the
/// filter drops any pinned event below its switch whatever override let it through.
/// </remarks>
public sealed class ThirdPartyLogSwitches
{
    /// <summary>Each pin with the switch that holds its namespace.</summary>
    private readonly IReadOnlyList<(ThirdPartyLogPin Pin, LoggingLevelSwitch Switch)> _pinned;

    /// <summary>Creates a switch per pin, each starting at its floor.</summary>
    /// <param name="pins">The pins to hold; each namespace appears once.</param>
    /// <exception cref="ArgumentException">Two pins name the same namespace.</exception>
    public ThirdPartyLogSwitches(IReadOnlyList<ThirdPartyLogPin> pins)
    {
        var distinct = pins.Select(pin => { return pin.Namespace; }).Distinct(StringComparer.Ordinal);
        if (distinct.Count() != pins.Count)
        {
            throw new ArgumentException("A namespace is pinned twice.", nameof(pins));
        }

        _pinned = pins.Select(pin => { return (pin, new LoggingLevelSwitch(pin.Floor)); }).ToList();
    }

    /// <summary>Moves every pinned namespace to the more severe of its floor and the panel's level.</summary>
    /// <param name="panelLevel">The panel's own minimum level.</param>
    public void Follow(LogEventLevel panelLevel)
    {
        foreach (var (pin, levelSwitch) in _pinned)
        {
            levelSwitch.MinimumLevel = (LogEventLevel)Math.Max((int)pin.Floor, (int)panelLevel);
        }
    }

    /// <summary>Registers the pins: an override per namespace, and the backstop filter.</summary>
    /// <param name="configuration">The logger configuration, after configuration-file overrides were read.</param>
    /// <returns>The same configuration, for chaining.</returns>
    public LoggerConfiguration ApplyTo(LoggerConfiguration configuration)
    {
        foreach (var (pin, levelSwitch) in _pinned)
        {
            configuration.MinimumLevel.Override(pin.Namespace, levelSwitch);
        }

        return configuration.Filter.ByExcluding(Refuses);
    }

    /// <summary>Says whether the event comes from a pinned namespace at a level below its switch.</summary>
    /// <param name="logEvent">The event about to be written.</param>
    /// <returns>True when the event must be dropped.</returns>
    private bool Refuses(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
            || value is not ScalarValue { Value: string sourceContext })
        {
            return false;
        }

        (ThirdPartyLogPin Pin, LoggingLevelSwitch Switch)? match = null;
        foreach (var candidate in _pinned)
        {
            if (candidate.Pin.Covers(sourceContext)
                && (match is null || candidate.Pin.Namespace.Length > match.Value.Pin.Namespace.Length))
            {
                match = candidate;
            }
        }

        return match is not null && logEvent.Level < match.Value.Switch.MinimumLevel;
    }
}
