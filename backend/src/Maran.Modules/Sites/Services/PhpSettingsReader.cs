using System.Globalization;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Builds the screen's view of an account's PHP settings: every setting a customer may change, what
/// they chose, what the pool carries after the plan's bound, and the bound itself (issue #138).
/// </summary>
/// <remarks>
/// <para>
/// <b>The catalogue travels with the answer.</b> The screen holds no setting name, unit or bound
/// (rules/architecture.md "The backend owns the data, the SPA renders it"): a setting added here appears
/// on the page with no change to the SPA. Labels and units are resolved in the caller's language.
/// </para>
/// <para>
/// <b>Reads through the request's own tenant-scoped context</b> and counts only what the caller can
/// see, so it is for requests; the pool writers that run for somebody else use
/// <see cref="PhpAccountPoolSettings"/>.
/// </para>
/// </remarks>
public sealed class PhpSettingsReader
{
    /// <summary>The prefix a setting's label key carries in the display-name resources.</summary>
    private const string LabelPrefix = "PhpSetting";

    /// <summary>The key naming the unit of a size, mebibytes.</summary>
    private const string MebibytesUnitKey = "PhpSettingUnitMebibytes";

    /// <summary>The key naming the unit of a duration, seconds.</summary>
    private const string SecondsUnitKey = "PhpSettingUnitSeconds";

    /// <summary>The wire word for each kind.</summary>
    private static readonly Dictionary<PhpSettingKind, string> KindWords = new Dictionary<PhpSettingKind, string>
    {
        [PhpSettingKind.Size] = "size",
        [PhpSettingKind.Seconds] = "seconds",
        [PhpSettingKind.Count] = "count",
        [PhpSettingKind.Timezone] = "timezone",
    };

    /// <summary>The zone names the server knows, shaped as the agent accepts them, sorted once.</summary>
    private static readonly Lazy<IReadOnlyList<string>> Timezones = new(() =>
    {
        return TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => { return zone.Id; })
            .Where(PhpSettingSpelling.IsTimezoneShape)
            .Order(StringComparer.Ordinal)
            .ToList();
    });

    /// <summary>The Sites module's database context, tenant-scoped to the caller.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Resolves labels and units in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="displayNames">Resolves labels and units in the request's culture.</param>
    public PhpSettingsReader(SitesDbContext dbContext, IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
    }

    /// <summary>Builds the view of one account's settings.</summary>
    /// <param name="account">The account, carrying the plan's bounds.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>Every setting with the customer's choice, the effective value and the plan's bound.</returns>
    public async Task<PhpSettingsDto> ReadAsync(AccountSnapshot account, CancellationToken cancellationToken)
    {
        var stored = await _dbContext.PhpSettings
            .AsNoTracking()
            .Where(setting => setting.AccountId == account.Id)
            .ToListAsync(cancellationToken);
        var effective = PhpSettingsPlanClamp.Apply(stored, account.PhpLimits)
            .ToDictionary(setting => { return setting.Name; }, StringComparer.Ordinal);
        var chosen = stored.ToDictionary(setting => { return setting.Name; }, StringComparer.Ordinal);

        var items = new List<PhpSettingItemDto>();
        foreach (var name in PhpSettingNames.All)
        {
            var kind = PhpSettingCatalogue.KindOf(name);
            var maximum = PhpSettingCatalogue.PlanMaximum(name, account.PhpLimits);
            chosen.TryGetValue(name, out var row);
            effective.TryGetValue(name, out var carried);
            var value = row is null ? null : PhpSettingSpelling.ToWire(kind, row.Value);

            items.Add(new PhpSettingItemDto(
                name,
                _displayNames[LabelPrefix + Pascal(name)].Value,
                KindWords[kind],
                UnitOf(kind),
                value,
                carried is null ? null : PhpSettingSpelling.ToWire(kind, carried.Value),
                maximum,
                Exceeds(value, maximum),
                kind == PhpSettingKind.Timezone ? Timezones.Value : null));
        }

        var sites = await _dbContext.Sites.CountAsync(
            site => site.AccountId == account.Id && site.BackendType == SiteBackendType.Php,
            cancellationToken);

        return new PhpSettingsDto(account.Id, items, sites);
    }

    /// <summary>Whether a chosen value is above the plan's bound.</summary>
    /// <param name="value">The chosen value in the wire spelling, or null.</param>
    /// <param name="maximum">The plan's bound, or null when none governs the setting.</param>
    /// <returns>True when both exist and the value is the larger.</returns>
    private static bool Exceeds(string? value, int? maximum)
    {
        return value is not null
            && maximum is not null
            && PhpSettingSpelling.TryReadNumber(value, out var number)
            && number > maximum;
    }

    /// <summary>Turns a php.ini name into the PascalCase tail of its label key.</summary>
    /// <param name="name">A setting name such as <c>upload_max_filesize</c> or <c>date.timezone</c>.</param>
    /// <returns>The same words capitalised and joined, such as <c>UploadMaxFilesize</c>.</returns>
    private static string Pascal(string name)
    {
        return string.Concat(name
            .Split('_', '.')
            .Select(word =>
            {
                return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..];
            }));
    }

    /// <summary>Names the unit of a kind in the caller's language.</summary>
    /// <param name="kind">The setting's kind.</param>
    /// <returns>The unit word, or null for a kind with none.</returns>
    private string? UnitOf(PhpSettingKind kind)
    {
        return kind switch
        {
            PhpSettingKind.Size => _displayNames[MebibytesUnitKey].Value,
            PhpSettingKind.Seconds => _displayNames[SecondsUnitKey].Value,
            _ => null,
        };
    }
}
