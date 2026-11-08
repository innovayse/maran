using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Domain.Enums;
using AgentPhpExtensionDto = Maran.Agent.Client.Services.PhpService.PhpExtensionDto;
using PhpExtensionDto = Maran.Modules.Sites.Common.PhpExtensionDto;

namespace Maran.Modules.Sites.Mappers;

/// <summary>
/// Restates the agent client's view of one PHP extension as the panel's outward
/// <see cref="PhpExtensionDto"/>.
/// </summary>
/// <remarks>
/// Two records share the name <c>PhpExtensionDto</c> — the agent client's and this module's — so the
/// aliases above name which is meant. The agent client's availability is restated once here, one
/// answer for one answer, and withheld altogether from a caller who cannot switch anything.
/// </remarks>
public static class PhpExtensionMapper
{
    /// <summary>Restates one extension for an administrator, availability included.</summary>
    /// <param name="extension">The agent client's extension.</param>
    /// <param name="label">The extension as the reader's language names it.</param>
    /// <param name="availabilityName">
    /// Words an availability in the reader's language. It is handed in so the mapper holds no resource
    /// lookup: the caller owns the localizer.
    /// </param>
    /// <returns>The extension in the outward shape.</returns>
    public static PhpExtensionDto ForAdministrator(
        AgentPhpExtensionDto extension,
        string label,
        Func<ExtensionAvailability, string> availabilityName)
    {
        var availability = ToAvailability(extension.Availability);

        return new PhpExtensionDto(extension.Name, label, extension.Enabled, availability, availabilityName(availability));
    }

    /// <summary>Restates one extension for a customer, who is told nothing about the host's packages.</summary>
    /// <param name="extension">The agent client's extension.</param>
    /// <param name="label">The extension as the reader's language names it.</param>
    /// <returns>The extension in the outward shape, availability and its word both <see langword="null"/>.</returns>
    public static PhpExtensionDto ForCustomer(AgentPhpExtensionDto extension, string label)
    {
        return new PhpExtensionDto(extension.Name, label, extension.Enabled, null, null);
    }

    /// <summary>Restates the agent client's availability as the panel's, one answer for one answer.</summary>
    /// <param name="availability">The agent client's availability.</param>
    /// <returns>The panel's availability with the same meaning; "not stated" stays "not stated".</returns>
    /// <exception cref="ArgumentOutOfRangeException">The agent client added a value this mapper does not know.</exception>
    private static ExtensionAvailability ToAvailability(PhpExtensionAvailability availability)
    {
        return availability switch
        {
            PhpExtensionAvailability.Unknown => ExtensionAvailability.Unknown,
            PhpExtensionAvailability.Available => ExtensionAvailability.Available,
            PhpExtensionAvailability.DisableNotSupported => ExtensionAvailability.DisableNotSupported,
            PhpExtensionAvailability.NotPackaged => ExtensionAvailability.NotPackaged,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, "Unknown PHP extension availability."),
        };
    }
}
