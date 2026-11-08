using System.Globalization;

namespace Maran.Agent.Simulated.Services.PhpService;

/// <summary>
/// The PHP versions the simulated host's package repository offers, and how versions are ordered.
/// </summary>
public static class SimulatedPhpCatalog
{
    /// <summary>The versions an install may name, newest first.</summary>
    public static readonly IReadOnlyList<string> Installable = ["8.4", "8.3", "8.2", "8.1", "8.0", "7.4"];

    /// <summary>The extensions the repository packages for every version, alphabetical.</summary>
    public static readonly IReadOnlyList<string> Extensions =
    [
        "bcmath",
        "curl",
        "gd",
        "imagick",
        "intl",
        "mbstring",
        "mysqli",
        "opcache",
        "pdo_mysql",
        "pdo_pgsql",
        "pgsql",
        "redis",
        "soap",
        "xml",
        "zip",
    ];

    /// <summary>The extensions a freshly installed version has loaded.</summary>
    public static readonly IReadOnlyList<string> DefaultEnabledExtensions =
    [
        "curl",
        "gd",
        "intl",
        "mbstring",
        "mysqli",
        "opcache",
        "pdo_mysql",
        "xml",
        "zip",
    ];

    /// <summary>Orders two-component versions numerically, newest first: <c>8.10</c> before <c>8.9</c>.</summary>
    /// <param name="versions">Well-formed versions such as <c>8.3</c>.</param>
    /// <returns>The same versions, newest first.</returns>
    public static IReadOnlyList<string> NewestFirst(IEnumerable<string> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        return versions
            .OrderByDescending(version => { return Number(version, 0); })
            .ThenByDescending(version => { return Number(version, 1); })
            .ToList();
    }

    /// <summary>Reads one component of a two-component version.</summary>
    /// <param name="version">A well-formed version.</param>
    /// <param name="component">0 for the major component, 1 for the minor.</param>
    /// <returns>The component's value, or zero when it is not a number.</returns>
    private static int Number(string version, int component)
    {
        var parts = version.Split('.');

        if (component >= parts.Length)
        {
            return 0;
        }

        var parsed = int.TryParse(parts[component], NumberStyles.None, CultureInfo.InvariantCulture, out var number);

        return parsed ? number : 0;
    }
}
