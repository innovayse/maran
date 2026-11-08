using System.Text.RegularExpressions;

namespace Maran.ArchitectureTests;

/// <summary>
/// Finds hand-written SQL, at the sites marked <c>// raw-sql:</c>, that names a table in a schema the
/// module does not own.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it looks for.</b> rules/csharp.md requires hand-written SQL to quote PascalCase
/// identifiers (<c>accounts."Accounts"</c>), so a foreign table reference has one spelling: a schema
/// name, a dot, then a quote — written plainly or escaped inside a C# string. That is narrow on
/// purpose. A schema name is also an ordinary word (<c>sites.Where(…)</c> is a variable), and a
/// scanner that matched the bare word would flag the tree for its identifiers.
/// </para>
/// <para>
/// UNOBSERVED HERE: SQL in a file that carries no <c>raw-sql</c> marker (the marker is how a raw-SQL
/// site is approved, so an unmarked one is a different defect), SQL assembled from variables, and a
/// foreign table named without the quotes the rule requires. The positive control in the tests
/// proves the scanner finds the spelling it claims to.
/// </para>
/// </remarks>
public static class ModuleRawSqlScanner
{
    /// <summary>The marker that makes a file a raw-SQL site (rules/csharp.md "Data and tenancy").</summary>
    private const string Marker = "raw-sql";

    /// <summary>Names the foreign schemas a marked source file's SQL references.</summary>
    /// <param name="source">The source text of one file.</param>
    /// <param name="foreignSchemas">Every schema the module does NOT own.</param>
    /// <returns>The schemas referenced, ordered; empty when the file is unmarked or references none.</returns>
    public static IReadOnlyList<string> ForeignReferences(string source, IReadOnlyCollection<string> foreignSchemas)
    {
        if (!source.Contains(Marker, StringComparison.Ordinal))
        {
            return [];
        }

        return foreignSchemas
            .Where(schema =>
            {
                var pattern = $@"(?<![\w.]){Regex.Escape(schema)}\s*\.\s*\\?""";
                return Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant);
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Scans every source file under a module's folder.</summary>
    /// <param name="moduleFolder">Absolute path of <c>backend/src/Maran.Modules/&lt;Name&gt;</c>.</param>
    /// <param name="foreignSchemas">Every schema the module does NOT own.</param>
    /// <returns>One line per file and schema, naming both.</returns>
    public static IReadOnlyList<string> Scan(string moduleFolder, IReadOnlyCollection<string> foreignSchemas)
    {
        var separator = Path.DirectorySeparatorChar;
        var found = new List<string>();

        foreach (var path in Directory.EnumerateFiles(moduleFolder, "*.cs", SearchOption.AllDirectories)
                     .Where(file =>
                     {
                         return !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                             && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
                     })
                     .Order(StringComparer.Ordinal))
        {
            foreach (var schema in ForeignReferences(File.ReadAllText(path), foreignSchemas))
            {
                found.Add($"{Path.GetRelativePath(moduleFolder, path)} names schema '{schema}'");
            }
        }

        return found;
    }
}
