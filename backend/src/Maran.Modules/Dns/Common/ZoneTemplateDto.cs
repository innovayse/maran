namespace Maran.Modules.Dns.Common;

/// <summary>One zone template as the administrator's list shows it.</summary>
/// <param name="Id">The template's identity, and the only identifier a request may name for it.</param>
/// <param name="Name">The template's name.</param>
/// <param name="Description">What the template is for, or empty.</param>
/// <param name="IsBuiltIn">Whether the template ships with the panel and so cannot be deleted.</param>
/// <param name="IsDefault">Whether a zone created for a new site is made from this template.</param>
/// <param name="RecordCount">How many records the template holds.</param>
/// <param name="UpdatedAt">When the template last changed.</param>
public sealed record ZoneTemplateDto(
    Guid Id,
    string Name,
    string Description,
    bool IsBuiltIn,
    bool IsDefault,
    int RecordCount,
    DateTimeOffset UpdatedAt);
