using System.Reflection;
using System.Resources;

namespace Maran.Sdk.Tests.Fixtures;

/// <summary>
/// Discovers, from the assemblies the panel actually ships, every machine-stable error code the
/// backend can return. A code IS the key of its <c>Resources/ErrorMessages.resx</c> entry
/// (<see cref="Maran.SharedKernel.Results.Error"/>), and every module embeds that resx into its own
/// assembly, so the embedded resource is the artefact the running panel reads — not a copy of it,
/// not a list retyped in a test.
/// </summary>
/// <remarks>
/// Discovery is deliberately by enumeration rather than by a hard-coded list of modules. A module
/// added to the panel brings its codes into this census on the next build without anyone
/// remembering to update a test, which is the only arrangement under which
/// <c>ApiResultExtensionsStatusCodeTests</c> can claim to cover the whole surface.
/// </remarks>
public static class ErrorCodeCensus
{
    /// <summary>Suffix of the embedded resource that holds one assembly's error-code table.</summary>
    private const string ErrorMessagesResourceSuffix = ".Resources.ErrorMessages.resources";

    /// <summary>File-name pattern of the assemblies that may carry such a resource.</summary>
    private const string PanelAssemblyPattern = "Maran.*.dll";

    /// <summary>Reads every error code the shipped assemblies declare, grouped by declaring assembly.</summary>
    /// <returns>Assembly simple name mapped to the codes it declares, in a stable order.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByAssembly()
    {
        var found = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, PanelAssemblyPattern).OrderBy(p => { return p; }, StringComparer.Ordinal))
        {
            var assembly = TryLoad(path);
            if (assembly is null)
            {
                continue;
            }

            var codes = ReadCodes(assembly);
            if (codes.Count > 0)
            {
                found[assembly.GetName().Name!] = codes;
            }
        }

        return found;
    }

    /// <summary>Reads every error code the shipped assemblies declare, flattened and de-duplicated.</summary>
    /// <returns>Every declared code, ordinally sorted.</returns>
    public static IReadOnlyList<string> AllCodes()
    {
        return ByAssembly()
            .SelectMany(entry => { return entry.Value; })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => { return code; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Loads one candidate assembly, ignoring anything that is not a managed panel assembly.</summary>
    /// <param name="path">Full path of the candidate file.</param>
    /// <returns>The loaded assembly, or null when the file cannot be loaded as one.</returns>
    private static Assembly? TryLoad(string path)
    {
        try
        {
            return Assembly.LoadFrom(path);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
    }

    /// <summary>Enumerates the keys of an assembly's embedded neutral error-message table.</summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The codes it declares, ordinally sorted; empty when it declares none.</returns>
    private static List<string> ReadCodes(Assembly assembly)
    {
        var codes = new List<string>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(ErrorMessagesResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            using var reader = new ResourceReader(stream);
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                if (entry.Key is string key)
                {
                    codes.Add(key);
                }
            }
        }

        codes.Sort(StringComparer.Ordinal);
        return codes;
    }
}
