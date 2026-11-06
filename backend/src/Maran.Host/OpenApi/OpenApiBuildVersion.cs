using System.Reflection;

namespace Maran.Host.OpenApi;

/// <summary>
/// The version the OpenAPI document states, read from the assembly the panel is built from.
/// </summary>
/// <remarks>
/// The informational version carries the source revision after a <c>+</c> (<c>1.0.0-beta.4+144f46c4</c>),
/// and that suffix changes with every commit. A document that stated it would differ on every build
/// of an unchanged API, so the committed baseline would churn and the breaking-change gate would
/// have a diff to read where there is none. Only the part before the <c>+</c> is the API's version.
/// </remarks>
public static class OpenApiBuildVersion
{
    /// <summary>The version stated when an assembly carries no informational version at all.</summary>
    public const string Unversioned = "0.0.0";

    /// <summary>Reads the version of an assembly.</summary>
    /// <param name="assembly">The assembly the panel's controllers are composed into.</param>
    /// <returns>The informational version without its revision suffix.</returns>
    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return WithoutRevision(informational);
    }

    /// <summary>Strips the source revision from an informational version.</summary>
    /// <param name="informationalVersion">The raw informational version, or null or blank when there is none.</param>
    /// <returns>The version before the first <c>+</c>, or <see cref="Unversioned"/> when nothing is left.</returns>
    public static string WithoutRevision(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return Unversioned;
        }

        var version = informationalVersion.Split('+')[0].Trim();
        return version.Length == 0 ? Unversioned : version;
    }
}
