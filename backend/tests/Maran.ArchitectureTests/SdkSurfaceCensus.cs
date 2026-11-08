using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads, without judging, what <c>Maran.Sdk</c> exports and which of its types each module reaches.
/// </summary>
/// <remarks>
/// The module side reads the metadata TypeRef table of the compiled module, so a base type, a field
/// or parameter type, an attribute and a call target are all seen, which a namespace-level
/// dependency check does not list per type. UNOBSERVED HERE: Sdk types reached only through
/// reflection or as a generic argument the compiler did not record as a TypeRef; non-public Sdk
/// members; a module reaching the Sdk through <c>Maran.SharedKernel</c>.
/// </remarks>
public static class SdkSurfaceCensus
{
    /// <summary>The simple name of the assembly whose surface is guarded.</summary>
    private const string SdkAssemblyName = "Maran.Sdk";

    /// <summary>The public, top-level, hand-written types of the Sdk.</summary>
    /// <param name="sdk">The <c>Maran.Sdk</c> assembly.</param>
    /// <returns>Their full names, sorted ordinally.</returns>
    public static List<string> ExportedSurface(Assembly sdk)
    {
        return sdk.GetExportedTypes()
            .Where(type =>
            {
                return !type.IsNested && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
            })
            .Select(type =>
            {
                return type.FullName!;
            })
            .OrderBy(name =>
            {
                return name;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The Sdk types a compiled module refers to.</summary>
    /// <param name="module">A module assembly.</param>
    /// <returns>The full names of the distinct <c>Maran.Sdk</c> types it references, sorted ordinally.</returns>
    public static List<string> SdkTypesUsedBy(Assembly module)
    {
        using var stream = File.OpenRead(module.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var used = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in reader.TypeReferences)
        {
            var typeRef = reader.GetTypeReference(handle);
            if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
            {
                continue;
            }

            var scope = reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope);
            if (!string.Equals(reader.GetString(scope.Name), SdkAssemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            used.Add(reader.GetString(typeRef.Namespace) + "." + reader.GetString(typeRef.Name));
        }

        return used.ToList();
    }
}
