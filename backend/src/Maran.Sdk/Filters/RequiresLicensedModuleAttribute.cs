namespace Maran.Sdk.Filters;

/// <summary>
/// Marks a controller as belonging to a module that is not <see cref="Contracts.LicenceTier.Included"/>:
/// a request is refused unless the installed licence covers <see cref="ModuleId"/>, whatever the menu
/// shows and whoever the caller is. One attribute line on the class is the whole opt-in; the
/// architecture test <c>LicensedModuleDeclarationTests</c> fails when a controller of a paid module
/// does not carry it.
/// </summary>
/// <remarks>
/// Built on <see cref="TypeFilterAttribute"/> so the filter is constructed per request with the module
/// id as its argument; see <see cref="RequiresLicensedModuleFilter"/> for who is refused and with what.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresLicensedModuleAttribute : TypeFilterAttribute
{
    /// <summary>The module whose licence cover the request needs.</summary>
    public string ModuleId { get; }

    /// <summary>Creates the attribute for one module.</summary>
    /// <param name="moduleId">The module's <see cref="Contracts.Manifest.Id"/>.</param>
    public RequiresLicensedModuleAttribute(string moduleId)
        : base(typeof(RequiresLicensedModuleFilter))
    {
        ModuleId = moduleId;
        Arguments = new object[] { moduleId };
    }
}
