namespace Maran.Modules.Marketplace.Models;

/// <summary>A machine key paired with the name the service already localised for the administrator.</summary>
/// <param name="Key">The stable key, e.g. an agent capability or a permission policy name.</param>
/// <param name="DisplayName">The name to show, in the language the request asked for.</param>
public sealed record CatalogueNamedItem(string Key, string DisplayName);
