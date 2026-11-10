using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>Decides whether a zone template may be deleted: never a built-in one, never the default.</summary>
public static class ZoneTemplateDeletionPolicy
{
    /// <summary>Weighs a deletion.</summary>
    /// <param name="template">The template to delete.</param>
    /// <param name="defaultTemplateId">The template new sites get their zone from, or null when none is chosen.</param>
    /// <returns>
    /// <see cref="TemplateDeletionVerdict.Allowed"/>, or the reason it is refused. A built-in template is named
    /// first, because making another the default would not make it deletable.
    /// </returns>
    public static TemplateDeletionVerdict Check(ZoneTemplate template, Guid? defaultTemplateId)
    {
        if (template.IsBuiltIn)
        {
            return TemplateDeletionVerdict.BuiltIn;
        }

        return defaultTemplateId == template.Id ? TemplateDeletionVerdict.IsDefault : TemplateDeletionVerdict.Allowed;
    }
}
