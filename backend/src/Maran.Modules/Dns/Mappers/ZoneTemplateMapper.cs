using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;

namespace Maran.Modules.Dns.Mappers;

/// <summary>Restates a <see cref="ZoneTemplate"/> as the wire shapes a screen reads. Translates; never decides.</summary>
public static class ZoneTemplateMapper
{
    /// <summary>Maps a template for the list.</summary>
    /// <param name="template">The template; its records need not be loaded.</param>
    /// <param name="recordCount">How many records the template holds.</param>
    /// <param name="defaultTemplateId">The template new sites get their zone from, or null.</param>
    /// <returns>The wire shape.</returns>
    public static ZoneTemplateDto From(ZoneTemplate template, int recordCount, Guid? defaultTemplateId)
    {
        return new ZoneTemplateDto(
            template.Id,
            template.Name,
            template.Description,
            template.IsBuiltIn,
            template.Id == defaultTemplateId,
            recordCount,
            template.UpdatedAt);
    }

    /// <summary>Maps a template with its records for the editor.</summary>
    /// <param name="template">The template, with its records loaded.</param>
    /// <param name="defaultTemplateId">The template new sites get their zone from, or null.</param>
    /// <returns>The wire shape.</returns>
    public static ZoneTemplateDetailDto DetailFrom(ZoneTemplate template, Guid? defaultTemplateId)
    {
        return new ZoneTemplateDetailDto(
            template.Id,
            template.Name,
            template.Description,
            template.IsBuiltIn,
            template.Id == defaultTemplateId,
            template.UpdatedAt,
            template.Records.Select(record =>
            {
                return new ZoneTemplateRecordDto(
                    record.Name,
                    record.Type,
                    record.Type.ToString().ToUpperInvariant(),
                    record.Ttl,
                    record.Value,
                    record.Priority,
                    record.Weight,
                    record.Port,
                    record.Flags,
                    record.Tag);
            }).ToList());
    }

    /// <summary>Maps a template for the creation form, which is never shown its records.</summary>
    /// <param name="id">The template's identity.</param>
    /// <param name="name">The template's name.</param>
    /// <param name="defaultTemplateId">The template new sites get their zone from, or null.</param>
    /// <returns>The wire shape.</returns>
    public static ZoneTemplateChoiceDto ChoiceFrom(Guid id, string name, Guid? defaultTemplateId)
    {
        return new ZoneTemplateChoiceDto(id, name, id == defaultTemplateId);
    }
}
