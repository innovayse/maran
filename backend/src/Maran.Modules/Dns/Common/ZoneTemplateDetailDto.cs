namespace Maran.Modules.Dns.Common;

/// <summary>One zone template with its records, as the administrator's editor shows it.</summary>
/// <param name="Id">The template's identity.</param>
/// <param name="Name">The template's name.</param>
/// <param name="Description">What the template is for, or empty.</param>
/// <param name="IsBuiltIn">Whether the template ships with the panel and so cannot be deleted.</param>
/// <param name="IsDefault">Whether a zone created for a new site is made from this template.</param>
/// <param name="UpdatedAt">When the template last changed.</param>
/// <param name="Records">The template's records, in the order a zone is created with them.</param>
public sealed record ZoneTemplateDetailDto(
    Guid Id,
    string Name,
    string Description,
    bool IsBuiltIn,
    bool IsDefault,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ZoneTemplateRecordDto> Records);
