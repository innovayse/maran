using System.Globalization;
using System.Text.Json;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Cron.Queries.GetCronOptions;

/// <summary>
/// Handles <see cref="GetCronOptionsQuery"/> by naming each closed set in the request's culture.
/// </summary>
/// <remarks>
/// <para>
/// The numeric sets (hours, minutes, days of the month) are generated from the bounds
/// <c>CronScheduleValidator</c> enforces, so a list can offer nothing the validator would refuse.
/// Their labels are zero-padded clock text for hours and minutes, which reads the same in every
/// language and therefore needs no resource entry.
/// </para>
/// <para>
/// The wire value of a frequency or an outcome is the enum member's camelCase name — the same
/// spelling the panel's JSON enum converter writes everywhere else — so a value read from this list
/// and a value read from a run listing are one vocabulary.
/// </para>
/// </remarks>
public sealed class GetCronOptionsQueryHandler
{
    /// <summary>The first hour of the day.</summary>
    private const int FirstHour = 0;

    /// <summary>The last hour of the day.</summary>
    private const int LastHour = 23;

    /// <summary>The first minute of an hour.</summary>
    private const int FirstMinute = 0;

    /// <summary>The last minute of an hour.</summary>
    private const int LastMinute = 59;

    /// <summary>The first day of a month.</summary>
    private const int FirstDayOfMonth = 1;

    /// <summary>The last day a month can have.</summary>
    private const int LastDayOfMonth = 31;

    /// <summary>Format that pads a clock figure to two digits.</summary>
    private const string TwoDigits = "00";

    /// <summary>Resolves this module's display names for the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public GetCronOptionsQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns every choice list, named for the request's culture.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the lists; this operation never fails.</returns>
    public Task<Result<CronOptionsDto>> HandleAsync(GetCronOptionsQuery query, CancellationToken cancellationToken)
    {
        var options = new CronOptionsDto(
            NamedEnum<CronFrequency>("CronFrequency"),
            Weekdays(),
            Numbers(FirstHour, LastHour, TwoDigits),
            Numbers(FirstMinute, LastMinute, TwoDigits),
            Numbers(FirstDayOfMonth, LastDayOfMonth, null),
            NamedEnum<CronRunOutcome>("CronRunOutcome"));

        return Task.FromResult(Result<CronOptionsDto>.Ok(options));
    }

    /// <summary>Lists every member of an enum, valued by its camelCase name and labelled from the resources.</summary>
    /// <typeparam name="TEnum">The enum whose members are offered, in declaration order.</typeparam>
    /// <param name="keyPrefix">The resource key prefix; the member's name completes the key.</param>
    /// <returns>One choice per member.</returns>
    private List<CronChoiceDto> NamedEnum<TEnum>(string keyPrefix)
        where TEnum : struct, Enum
    {
        return Enum.GetNames<TEnum>()
            .Select(name =>
            {
                return new CronChoiceDto(
                    JsonNamingPolicy.CamelCase.ConvertName(name),
                    _displayNames[keyPrefix + name].Value);
            })
            .ToList();
    }

    /// <summary>Lists the seven weekdays, valued by cron's number for them (Sunday is 0).</summary>
    /// <returns>One choice per weekday, Sunday first.</returns>
    private List<CronChoiceDto> Weekdays()
    {
        return Enum.GetValues<DayOfWeek>()
            .Select(day =>
            {
                return new CronChoiceDto(
                    ((int)day).ToString(CultureInfo.InvariantCulture),
                    _displayNames["CronWeekday" + day].Value);
            })
            .ToList();
    }

    /// <summary>Lists an inclusive run of whole numbers.</summary>
    /// <param name="first">The first number.</param>
    /// <param name="last">The last number.</param>
    /// <param name="labelFormat">A numeric format for the label, or null to print the number as it is.</param>
    /// <returns>One choice per number, valued by the unpadded number cron expects.</returns>
    private static List<CronChoiceDto> Numbers(int first, int last, string? labelFormat)
    {
        return Enumerable.Range(first, last - first + 1)
            .Select(number =>
            {
                return new CronChoiceDto(
                    number.ToString(CultureInfo.InvariantCulture),
                    number.ToString(labelFormat, CultureInfo.InvariantCulture));
            })
            .ToList();
    }
}
