namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>The answer to "may this zone template be deleted".</summary>
public enum TemplateDeletionVerdict
{
    /// <summary>The template is the administrator's own and no zone is created from it by default.</summary>
    Allowed,

    /// <summary>The template ships with the panel and is only ever edited.</summary>
    BuiltIn,

    /// <summary>The template is the one new sites get their zone from; another must be made the default first.</summary>
    IsDefault,
}
