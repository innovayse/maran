namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>The answer to "may this record value of a template carry the variables it names".</summary>
public enum TemplateTokenVerdict
{
    /// <summary>The value carries no variable, or only variables its record type allows.</summary>
    Accepted,

    /// <summary>A brace is not part of a well-formed <c>{name}</c> token: unclosed, stray or nested.</summary>
    Unbalanced,

    /// <summary>A well-formed token names no variable of the closed catalogue.</summary>
    UnknownToken,

    /// <summary>A variable of the catalogue is named in a record type that does not allow it.</summary>
    NotAllowedHere,
}
