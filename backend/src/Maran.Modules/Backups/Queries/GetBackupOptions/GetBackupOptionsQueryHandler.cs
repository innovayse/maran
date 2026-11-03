using System.Globalization;
using System.Text.Json;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Backups.Queries.GetBackupOptions;

/// <summary>
/// Handles <see cref="GetBackupOptionsQuery"/> by naming each closed set in the request's culture.
/// </summary>
/// <remarks>
/// <para>
/// The hours and the retention counts are generated from the bounds
/// <c>SaveBackupScheduleCommandValidator</c> enforces — the entity's own constants — so a list can
/// offer nothing the validator would refuse. The retention list is the whole range rather than a
/// set of presets: a schedule saved earlier with any legal count must still find its value in the
/// list, or the form would open on a blank for a retention the server is applying.
/// </para>
/// <para>
/// The wire value of an enum member is its camelCase name — the spelling the panel's JSON enum
/// converter writes everywhere else — so a value read from this list and one read from a backup or a
/// schedule are one vocabulary.
/// </para>
/// </remarks>
public sealed class GetBackupOptionsQueryHandler
{
    /// <summary>Format that pads an hour to the two digits of a clock face.</summary>
    private const string TwoDigits = "00";

    /// <summary>The weekdays in the order a week is read: Monday first, Sunday last.</summary>
    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    /// <summary>Resolves this module's display names for the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public GetBackupOptionsQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns every choice list, named for the request's culture.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the lists; this operation never fails.</returns>
    public Task<Result<BackupOptionsDto>> HandleAsync(GetBackupOptionsQuery query, CancellationToken cancellationToken)
    {
        var options = new BackupOptionsDto(
            NamedEnum<BackupStatus>("BackupStatus"),
            NamedEnum<BackupKind>("BackupKind"),
            NamedEnum<BackupDestinationKind>("BackupDestinationKind"),
            NamedEnum<BackupFrequency>("BackupFrequency"),
            Weekdays(),
            Hours(),
            RetainCounts());

        return Task.FromResult(Result<BackupOptionsDto>.Ok(options));
    }

    /// <summary>Lists every member of an enum, valued by its camelCase name and labelled from the resources.</summary>
    /// <typeparam name="TEnum">The enum whose members are offered, in declaration order.</typeparam>
    /// <param name="keyPrefix">The resource key prefix; the member's name completes the key.</param>
    /// <returns>One choice per member.</returns>
    private List<BackupChoiceDto> NamedEnum<TEnum>(string keyPrefix)
        where TEnum : struct, Enum
    {
        return Enum.GetNames<TEnum>()
            .Select(name =>
            {
                return new BackupChoiceDto(
                    JsonNamingPolicy.CamelCase.ConvertName(name),
                    _displayNames[keyPrefix + name].Value);
            })
            .ToList();
    }

    /// <summary>Lists the weekdays, Monday first, valued by the camelCase name the schedule stores.</summary>
    /// <returns>One choice per weekday.</returns>
    private List<BackupChoiceDto> Weekdays()
    {
        return WeekOrder
            .Select(day =>
            {
                return new BackupChoiceDto(
                    JsonNamingPolicy.CamelCase.ConvertName(day.ToString()),
                    _displayNames["BackupWeekday" + day].Value);
            })
            .ToList();
    }

    /// <summary>Lists the hours a schedule may name, valued by the unpadded number the schedule stores.</summary>
    /// <returns>One choice per hour, labelled as a clock face.</returns>
    private static List<BackupChoiceDto> Hours()
    {
        var count = BackupSchedule.MaximumHourUtc - BackupSchedule.MinimumHourUtc + 1;
        return Enumerable.Range(BackupSchedule.MinimumHourUtc, count)
            .Select(hour =>
            {
                return new BackupChoiceDto(
                    hour.ToString(CultureInfo.InvariantCulture),
                    hour.ToString(TwoDigits, CultureInfo.InvariantCulture) + ":00");
            })
            .ToList();
    }

    /// <summary>Lists every retention count the validator accepts.</summary>
    /// <returns>One choice per count from the minimum to the ceiling.</returns>
    private static List<BackupChoiceDto> RetainCounts()
    {
        var span = BackupSchedule.MaximumRetainCount - BackupSchedule.MinimumRetainCount + 1;
        return Enumerable.Range(BackupSchedule.MinimumRetainCount, span)
            .Select(copies =>
            {
                var text = copies.ToString(CultureInfo.InvariantCulture);
                return new BackupChoiceDto(text, text);
            })
            .ToList();
    }
}
