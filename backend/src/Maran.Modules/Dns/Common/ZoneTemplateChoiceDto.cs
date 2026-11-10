namespace Maran.Modules.Dns.Common;

/// <summary>
/// One zone template as the zone-creation form offers it to whoever may create a zone: its name and whether it is
/// the default, and never its records, which only an administrator reads.
/// </summary>
/// <param name="Id">The template's identity, which the creation request names.</param>
/// <param name="Name">The template's name.</param>
/// <param name="IsDefault">Whether a zone created for a new site is made from this template.</param>
public sealed record ZoneTemplateChoiceDto(Guid Id, string Name, bool IsDefault);
