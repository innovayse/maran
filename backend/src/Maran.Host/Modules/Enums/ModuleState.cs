namespace Maran.Host.Modules.Enums;

/// <summary>
/// Whether a composed module may be used on this server right now. A module the panel did not compose
/// is simply absent from the catalogue, which is how the interface tells "not installed" from
/// "installed but not covered" (rules/vue.md "Modules in the frontend"). Serialized as its camelCase
/// member name (<c>"enabled"</c>, <c>"lapsed"</c>, <c>"locked"</c>) by the panel-wide
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>.
/// </summary>
public enum ModuleState
{
    /// <summary>Usable. The module's tier asks for no licence, or the licence covers it.</summary>
    Enabled,

    /// <summary>
    /// Composed and WAS covered, but the cover has ended (the licence expired, or its lease ran out): the
    /// interface treats it like <see cref="Locked"/> and the notice says it lapsed. The per-request gate
    /// refuses it exactly as it refuses a module that was never covered.
    /// </summary>
    Lapsed,

    /// <summary>
    /// Composed but not covered: the interface shows its entries as locked and leads them to the
    /// upgrade page rather than hiding the product's existence. This state only decides what the
    /// interface offers; the per-request licence check is the boundary and does not read it.
    /// </summary>
    Locked,
}
