using System.Text;

namespace Maran.ArchitectureTests;

/// <summary>
/// The committed record of the Sdk's public surface and of how many Sdk types each module may use,
/// as read from <c>Baselines/sdk-surface.txt</c>.
/// </summary>
/// <remarks>
/// Lines are <c>type &lt;full name&gt;</c>, <c>module &lt;Name&gt; &lt;ceiling&gt;</c> and
/// <c>total &lt;ceiling&gt;</c>; <c>#</c> starts a comment. A malformed or repeated line is refused by
/// naming it, so a hand edit that breaks the file cannot read as an empty record.
/// </remarks>
public sealed class SdkSurfaceBaseline
{
    /// <summary>The file this record is read from, relative to the test binary.</summary>
    public const string RelativePath = "Baselines/sdk-surface.txt";

    /// <summary>The exact public surface, as full type names.</summary>
    public IReadOnlySet<string> Types { get; }

    /// <summary>The most distinct Sdk types each module may use, keyed by module name.</summary>
    public IReadOnlyDictionary<string, int> ModuleCeilings { get; }

    /// <summary>The most distinct Sdk types all modules together may use.</summary>
    public int TotalCeiling { get; }

    /// <summary>Creates a record from its parts.</summary>
    /// <param name="types">The exact public surface.</param>
    /// <param name="moduleCeilings">Each module's ceiling.</param>
    /// <param name="totalCeiling">The ceiling over every module together.</param>
    public SdkSurfaceBaseline(IReadOnlySet<string> types, IReadOnlyDictionary<string, int> moduleCeilings, int totalCeiling)
    {
        Types = types;
        ModuleCeilings = moduleCeilings;
        TotalCeiling = totalCeiling;
    }

    /// <summary>Reads the committed record next to the test binary.</summary>
    /// <returns>The parsed record.</returns>
    public static SdkSurfaceBaseline Load()
    {
        return Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RelativePath)));
    }

    /// <summary>Parses the text of a record.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The parsed record.</returns>
    /// <exception cref="FormatException">A line is malformed or repeats an earlier one; the message names it.</exception>
    public static SdkSurfaceBaseline Parse(string text)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var modules = new Dictionary<string, int>(StringComparer.Ordinal);
        int? total = null;
        var number = 0;

        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bad = new FormatException($"{RelativePath} line {number} is malformed or repeated: '{line}'.");

            if (parts is ["type", var name])
            {
                if (!types.Add(name))
                {
                    throw bad;
                }
            }
            else if (parts is ["module", var module, var moduleCeiling] && int.TryParse(moduleCeiling, out var ceiling))
            {
                if (!modules.TryAdd(module, ceiling))
                {
                    throw bad;
                }
            }
            else if (parts is ["total", var totalText] && int.TryParse(totalText, out var totalValue) && total is null)
            {
                total = totalValue;
            }
            else
            {
                throw bad;
            }
        }

        return new SdkSurfaceBaseline(
            types,
            modules,
            total ?? throw new FormatException($"{RelativePath} has no 'total' line."));
    }

    /// <summary>Writes a record in the committed file's shape, for a reviewer to paste.</summary>
    /// <param name="surface">The surface to record.</param>
    /// <param name="usage">How many Sdk types each module uses.</param>
    /// <param name="total">How many distinct Sdk types all modules use together.</param>
    /// <returns>The file's lines (without its header comment).</returns>
    public static string Render(IEnumerable<string> surface, IReadOnlyDictionary<string, int> usage, int total)
    {
        var text = new StringBuilder();
        text.Append("total ").Append(total).Append('\n');
        foreach (var module in usage.OrderBy(
            pair =>
            {
                return pair.Key;
            },
            StringComparer.Ordinal))
        {
            text.Append("module ").Append(module.Key).Append(' ').Append(module.Value).Append('\n');
        }

        foreach (var name in surface.OrderBy(
            name =>
            {
                return name;
            },
            StringComparer.Ordinal))
        {
            text.Append("type ").Append(name).Append('\n');
        }

        return text.ToString();
    }
}
