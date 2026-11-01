using Serilog.Events;

namespace Maran.Host.Logging;

/// <summary>
/// The minimum level the panel's configuration asks for, read the way Serilog's own configuration
/// reader reads it, so the runtime switch starts where the configuration says (issue #131).
/// </summary>
/// <remarks>
/// Serilog accepts the level in two shapes — <c>Serilog:MinimumLevel</c> as a bare value, or
/// <c>Serilog:MinimumLevel:Default</c> beside an <c>Override</c> section — and an operator's
/// <c>panel.env</c> may use either (<c>Serilog__MinimumLevel__Default=Debug</c>). Both are read here.
/// A value that names no level falls back to <see cref="LogEventLevel.Information"/>, the level the
/// panel ran at before the switch existed, rather than refusing to start over a log setting.
/// </remarks>
public static class ConfiguredMinimumLevel
{
    /// <summary>The configuration key of the nested form.</summary>
    private const string DefaultKey = "Serilog:MinimumLevel:Default";

    /// <summary>The configuration key of the bare form.</summary>
    private const string BareKey = "Serilog:MinimumLevel";

    /// <summary>Reads the configured minimum level.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <returns>The configured level, or Information when none is configured or it is unreadable.</returns>
    public static LogEventLevel Read(IConfiguration configuration)
    {
        var configured = configuration[DefaultKey] ?? configuration[BareKey];

        // Enum.TryParse also accepts a number, which would let "99" through as a level that does
        // not exist; IsDefined closes that.
        if (Enum.TryParse<LogEventLevel>(configured, ignoreCase: true, out var level)
            && Enum.IsDefined(level))
        {
            return level;
        }

        return LogEventLevel.Information;
    }
}
