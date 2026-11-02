namespace Maran.Host.Modules.Enums;

/// <summary>
/// Whether a composed module may be used on this server right now. Two states and no third: a
/// module the panel did not compose is simply absent from the catalogue, which is how the interface
/// tells "not installed" from "installed but not covered" (rules/vue.md "Modules in the frontend").
/// Serialized as its camelCase member name (<c>"enabled"</c>, <c>"locked"</c>) by the panel-wide
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>.
/// </summary>
public enum ModuleState
{
    /// <summary>Usable. The module's tier asks for no licence, or the licence covers it.</summary>
    Enabled,

    /// <summary>
    /// Composed but not covered: the interface shows its entries as locked and leads them to the
    /// upgrade page rather than hiding the product's existence. This state only decides what the
    /// interface offers; the per-request licence check is the boundary and does not read it.
    /// </summary>
    Locked,
}
