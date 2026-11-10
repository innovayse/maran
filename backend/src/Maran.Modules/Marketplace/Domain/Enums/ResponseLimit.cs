namespace Maran.Modules.Marketplace.Domain.Enums;

/// <summary>
/// Which of the configured response-size caps applies to a marketplace answer. The request names the
/// KIND of answer it expects and the transport reads the number from the options, so no caller carries
/// a size and a cap cannot be forgotten.
/// </summary>
public enum ResponseLimit
{
    /// <summary>A catalogue, module detail, update report or key list: <c>MaxCatalogueBytes</c>.</summary>
    Catalogue,

    /// <summary>A download grant: <c>MaxGrantBytes</c>.</summary>
    Grant,
}
