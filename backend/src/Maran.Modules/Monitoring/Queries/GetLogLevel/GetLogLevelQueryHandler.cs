using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Monitoring.Queries.GetLogLevel;

/// <summary>
/// Handles <see cref="GetLogLevelQuery"/>: the running level plus every choice, named in the caller's
/// language (issue #131).
/// </summary>
/// <remarks>
/// The level is read from the running switch rather than from the stored row. On a panel where no
/// administrator has chosen, there is no row and the configured level is in force; after a choice,
/// the two agree. A screen that reported the row would show the panel's intention whatever the
/// logger was doing, which is the one thing a status read must not do.
/// </remarks>
public sealed class GetLogLevelQueryHandler
{
    /// <summary>The prefix of the display-name keys, one per <see cref="PanelLogLevel"/> member.</summary>
    private const string DisplayNameKeyPrefix = "LogLevel";

    /// <summary>The running logger's level.</summary>
    private readonly IPanelLogLevelSwitch _levelSwitch;

    /// <summary>The module's display names, resolved in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="levelSwitch">The running logger's level.</param>
    /// <param name="displayNames">The module's display names.</param>
    public GetLogLevelQueryHandler(IPanelLogLevelSwitch levelSwitch, IStringLocalizer<DisplayNames> displayNames)
    {
        _levelSwitch = levelSwitch;
        _displayNames = displayNames;
    }

    /// <summary>Reads the running level and the choices.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <returns>The level and the choices, always successfully — reading a switch cannot fail.</returns>
    public Result<LogLevelDto> Handle(GetLogLevelQuery query)
    {
        var choices = Enum.GetValues<PanelLogLevel>()
            .Select(level =>
            {
                return new LogLevelChoiceDto(level, NameOf(level));
            })
            .ToList();
        var current = _levelSwitch.Current;

        return Result<LogLevelDto>.Ok(new LogLevelDto(current, NameOf(current), choices));
    }

    /// <summary>Names a level in the request's culture.</summary>
    /// <param name="level">The level to name.</param>
    /// <returns>The level's display name.</returns>
    private string NameOf(PanelLogLevel level)
    {
        return _displayNames[DisplayNameKeyPrefix + level].Value;
    }
}
