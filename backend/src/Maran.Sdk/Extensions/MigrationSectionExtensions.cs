using System.Text.Json;
using Maran.Sdk.Contracts;

namespace Maran.Sdk.Extensions;

/// <summary>
/// Writes a module's payload into a <see cref="MigrationSection"/> and reads it back, with one spelling of the
/// JSON and one answer to a section that cannot be read (issue #127).
/// </summary>
/// <remarks>
/// A section is untrusted input on the target: it comes out of a signed package, but a valid signature proves
/// only who signed it. Reading therefore never throws and never guesses: a payload that is not the shape the
/// module wrote, of a version the module does not know, or that is the JSON <c>null</c>, reads as
/// <c>null</c>, and the importer answers <c>MigrationSectionInvalid</c>.
/// </remarks>
public static class MigrationSectionExtensions
{
    /// <summary>Writes a module's payload as its section.</summary>
    /// <typeparam name="T">The payload record the module owns.</typeparam>
    /// <param name="payload">What the module hands over: named fields only.</param>
    /// <param name="moduleId">The id of the module that owns the payload.</param>
    /// <param name="version">The version of the payload's shape.</param>
    /// <param name="resourceCount">How many resources of the module's kind the payload holds.</param>
    /// <returns>The section.</returns>
    public static MigrationSection ToSection<T>(this T payload, string moduleId, int version, int resourceCount = 0)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new MigrationSection(moduleId, version, JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), resourceCount);
    }

    /// <summary>Reads a section back into the payload record of the module that wrote it.</summary>
    /// <typeparam name="T">The payload record the module owns.</typeparam>
    /// <param name="section">The section of the package.</param>
    /// <param name="moduleId">The id of the module reading it; a section of another module reads as <c>null</c>.</param>
    /// <param name="version">The one version of the shape the module understands.</param>
    /// <returns>The payload, or <c>null</c> when the section is not exactly what the module wrote.</returns>
    public static T? ReadPayload<T>(this MigrationSection section, string moduleId, int version)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(section);

        if (!string.Equals(section.ModuleId, moduleId, StringComparison.Ordinal) || section.Version != version)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(section.Json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
