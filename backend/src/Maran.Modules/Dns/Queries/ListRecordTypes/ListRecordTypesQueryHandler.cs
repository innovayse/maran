using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Options;
using Maran.Modules.Dns.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Dns.Queries.ListRecordTypes;

/// <summary>
/// Handles <see cref="ListRecordTypesQuery"/>: the record types, which fields each uses, and the bounds, in the
/// viewer's language.
/// </summary>
/// <remarks>
/// The interface keeps no DNS constant of its own (rules/architecture.md "The backend owns the data, the SPA
/// renders it"), so everything a form needs comes from here, and which fields a type uses is read from
/// <see cref="RecordTypeFields"/>, the same statement the shape policy refuses a stray field by. A type's
/// label is the type as it is written in a zone, the same in every language; its description is translated.
/// </remarks>
public sealed class ListRecordTypesQueryHandler
{
    /// <summary>The viewer's-language lookup of <c>Resources/DisplayNames</c>.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>The operator's settings: how many records a zone may hold.</summary>
    private readonly IOptions<DnsOptions> _options;

    /// <summary>Creates the handler.</summary>
    /// <param name="names">The viewer's-language lookup of the module's display names.</param>
    /// <param name="options">The operator's settings.</param>
    public ListRecordTypesQueryHandler(IStringLocalizer<DisplayNames> names, IOptions<DnsOptions> options)
    {
        _names = names;
        _options = options;
    }

    /// <summary>Builds the catalogue.</summary>
    /// <param name="query">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The catalogue; this operation never fails.</returns>
    public Task<Result<RecordTypeCatalogDto>> HandleAsync(
        ListRecordTypesQuery query,
        CancellationToken cancellationToken)
    {
        var types = Enum.GetValues<RecordType>()
            .Select(type =>
            {
                return new RecordTypeDto(
                    type,
                    type.ToString().ToUpperInvariant(),
                    _names[$"DnsRecordType{type}Description"].Value,
                    RecordTypeFields.UsesPriority(type),
                    RecordTypeFields.UsesWeightAndPort(type),
                    RecordTypeFields.UsesFlagsAndTag(type));
            })
            .ToList();

        var catalog = new RecordTypeCatalogDto(
            types,
            DnsLimits.MinTtl,
            DnsLimits.MaxTtl,
            DnsLimits.DefaultTtl,
            DnsLimits.MaxValueLength,
            _options.Value.MaxRecordsPerZone,
            DnsLimits.CaaTags);

        return Task.FromResult(Result<RecordTypeCatalogDto>.Ok(catalog));
    }
}
