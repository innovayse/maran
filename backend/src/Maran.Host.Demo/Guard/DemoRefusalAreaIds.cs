using Maran.Host.Demo.Guard.Enums;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// The wire spelling of every <see cref="DemoRefusalArea"/>. It is also the key of the area's sentence in
/// <c>Resources/DemoMessages.resx</c>, which the status endpoint reads too, so the words exist once.
/// </summary>
public static class DemoRefusalAreaIds
{
    /// <summary>Every area, in declaration order.</summary>
    public static readonly IReadOnlyList<DemoRefusalArea> All = Enum.GetValues<DemoRefusalArea>();

    /// <summary>The camelCase id the SPA and the status endpoint use for an area.</summary>
    /// <param name="area">The area.</param>
    /// <returns>The id, for example <c>securityPolicy</c>.</returns>
    public static string IdOf(DemoRefusalArea area)
    {
        return area switch
        {
            DemoRefusalArea.SecurityPolicy => "securityPolicy",
            DemoRefusalArea.Smtp => "smtp",
            DemoRefusalArea.PanelCertificate => "panelCertificate",
            DemoRefusalArea.PanelUpdate => "panelUpdate",
            DemoRefusalArea.TwoFactor => "twoFactor",
            DemoRefusalArea.ApiKeys => "apiKeys",
            DemoRefusalArea.Licence => "licence",
            DemoRefusalArea.Credentials => "credentials",
            DemoRefusalArea.AccessPaths => "accessPaths",
            DemoRefusalArea.ExternalServices => "externalServices",
            DemoRefusalArea.ProtectedResource => "protectedResource",
            DemoRefusalArea.Unclassified => "unclassified",
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown demo refusal area."),
        };
    }

}
