using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.PhpService;

/// <summary>Reads the wire's PHP extension listing into the panel's own types.</summary>
internal static class PhpExtensionMapper
{
    /// <summary>Projects the wire extension list onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>ListPhpExtensions</c>.</param>
    /// <returns>The extensions in the order the agent sent them, alphabetical by name.</returns>
    internal static List<PhpExtensionDto> ToExtensions(ListPhpExtensionsOk ok)
    {
        var extensions = new List<PhpExtensionDto>(ok.Extensions.Count);
        foreach (var extension in ok.Extensions)
        {
            extensions.Add(new PhpExtensionDto(
                extension.Name,
                extension.Enabled,
                ToAvailability(extension.Availability)));
        }

        return extensions;
    }

    /// <summary>Projects the wire availability onto the panel's own.</summary>
    /// <param name="availability">The availability the agent reported.</param>
    /// <returns>The panel's value; <see cref="PhpExtensionAvailability.Unknown"/> for anything unnamed.</returns>
    /// <remarks>
    /// Written out rather than cast. UNSPECIFIED, and a value from an agent newer than this panel, is a
    /// reply that states nothing, so it is read ONCE here as unknown and never as "not packaged" or
    /// "available": the wire's zero must not become an action the panel offers or withholds.
    /// </remarks>
    private static PhpExtensionAvailability ToAvailability(Maran.Agent.V1.PhpExtensionAvailability availability)
    {
        return availability switch
        {
            Maran.Agent.V1.PhpExtensionAvailability.Available => PhpExtensionAvailability.Available,
            Maran.Agent.V1.PhpExtensionAvailability.DisableNotSupported => PhpExtensionAvailability.DisableNotSupported,
            Maran.Agent.V1.PhpExtensionAvailability.NotPackaged => PhpExtensionAvailability.NotPackaged,
            _ => PhpExtensionAvailability.Unknown,
        };
    }
}
