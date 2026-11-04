using System.Xml.Linq;

namespace Maran.ArchitectureTests;

/// <summary>
/// No shipped error sentence contains an en dash (U+2013) or an em dash (U+2014), in any language
/// (issue #378).
/// </summary>
/// <remarks>
/// <para>
/// The universe is every authored <c>ErrorMessages</c> table under <c>backend/src</c> in its three
/// files (neutral English, <c>.ru</c>, <c>.hy</c>), read through <see cref="ResourceTriples"/> as the
/// author wrote them, so a value is judged in the file that would be edited to fix it and a table a
/// new module adds is covered the day it exists. The failure names the table, the language and the
/// key, which is all an author needs to find the sentence.
/// </para>
/// <para>
/// Only <c>ErrorMessages</c> is judged: the sentence a failure shows a customer. Other resource
/// purposes (display names, mail templates) are not claimed by this law.
/// </para>
/// <para>
/// UNOBSERVED HERE: other dash-like characters (the minus sign U+2212, the horizontal bar U+2015, a
/// figure dash U+2012) and any text that does not come from an <c>ErrorMessages</c> table, such as a
/// validator message or a sentence composed in code.
/// </para>
/// </remarks>
public sealed class UserFacingDashTests
{
    /// <summary>The file name every module's error table carries.</summary>
    private const string ErrorTableFile = "ErrorMessages.resx";

    /// <summary>
    /// The fewest error tables the panel is known to ship; a floor just under the real figure, not a
    /// count. The real figure is 16; regenerate it with
    /// <c>find backend/src -name ErrorMessages.resx -not -path '*/obj/*' -not -path '*/bin/*' | wc -l</c>.
    /// </summary>
    private const int TableFloor = 14;

    /// <summary>
    /// The fewest sentences per language the census must read; a floor just under the real figure, not
    /// a count. The real figure is 294 in each language; regenerate it with
    /// <c>cat $(find backend/src -name ErrorMessages.resx -not -path '*/obj/*' -not -path '*/bin/*')
    /// | grep -c '&lt;data name='</c> (the same for the <c>.ru</c> and <c>.hy</c> siblings).
    /// </summary>
    private const int SentenceFloor = 250;

    /// <summary>The characters no user-facing error sentence may contain: U+2013 and U+2014.</summary>
    private static readonly char[] ForbiddenDashes = ['\u2013', '\u2014'];

    /// <summary>The languages every table ships, the neutral English file first.</summary>
    private static readonly string?[] Languages = [null, "ru", "hy"];

    /// <summary>Every shipped error sentence is free of en and em dashes, in every language.</summary>
    [Fact]
    public void No_shipped_error_sentence_contains_an_en_or_em_dash()
    {
        var offences = DashOffences(ShippedTables());

        Assert.True(
            offences.Count == 0,
            "These error sentences contain an en dash (U+2013) or an em dash (U+2014); rewrite each with "
            + "plain punctuation (comma, colon, full stop or a hyphen):"
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, offences)}");
    }

    /// <summary>
    /// The census is reading the tables it judges: it sees many tables and many sentences in every
    /// language, so an empty or moved source tree cannot pass it.
    /// </summary>
    [Fact]
    public void The_census_reads_the_error_tables_it_judges_in_every_language()
    {
        var tables = ShippedTables();

        Assert.True(
            tables
                .Select(table =>
                {
                    return table.Table;
                })
                .Distinct(StringComparer.Ordinal)
                .Count() >= TableFloor,
            $"Fewer than {TableFloor} ErrorMessages tables were found under backend/src, so the dash law "
            + "is reporting on almost nothing.");

        foreach (var language in Languages)
        {
            var sentences = tables
                .Where(table =>
                {
                    return table.Language == (language ?? "en");
                })
                .Sum(table =>
                {
                    return table.Entries.Count;
                });

            Assert.True(
                sentences >= SentenceFloor,
                $"Only {sentences} error sentences were read for '{language ?? "en"}', fewer than the "
                + $"{SentenceFloor} the dash law needs to be reporting on the panel.");
        }
    }

    /// <summary>The law refuses a table whose sentence carries either dash, naming key and language.</summary>
    [Fact]
    public void A_planted_en_or_em_dash_is_refused_naming_its_table_language_and_key()
    {
        var planted = new List<TableText>
        {
            new("Planted/ErrorMessages.resx", "ru", Entries(("PlantedEnDash", "Name \u2013 taken"))),
            new("Planted/ErrorMessages.resx", "hy", Entries(("PlantedEmDash", "Name \u2014 taken"))),
        };

        var offences = DashOffences(planted);

        Assert.Equal(2, offences.Count);
        Assert.Contains(
            offences,
            offence =>
            {
                return offence.Contains("[ru]", StringComparison.Ordinal)
                    && offence.Contains("PlantedEnDash", StringComparison.Ordinal);
            });
        Assert.Contains(
            offences,
            offence =>
            {
                return offence.Contains("[hy]", StringComparison.Ordinal)
                    && offence.Contains("PlantedEmDash", StringComparison.Ordinal);
            });
    }

    /// <summary>The law accepts plain punctuation, a hyphen-minus included, so it does not refuse everything.</summary>
    [Fact]
    public void Plain_punctuation_is_accepted()
    {
        var plain = new List<TableText>
        {
            new(
                "Planted/ErrorMessages.resx",
                "en",
                Entries(("PlainHyphen", "Use a well-formed name: letters, digits, '-' or '_'."))),
        };

        Assert.Empty(DashOffences(plain));
    }

    /// <summary>
    /// The census reads real files end to end: a temporary tree holding a neutral table and its
    /// <c>.ru</c> and <c>.hy</c> siblings, one value carrying an em dash, goes through the same reader
    /// the shipped tables do, and the offence comes out naming table, language and key.
    /// </summary>
    [Fact]
    public void A_dash_written_into_a_resx_file_is_found_by_the_census_reader()
    {
        var root = Path.Combine(Path.GetTempPath(), $"maran-dash-control-{Guid.NewGuid():N}");
        try
        {
            var directory = Path.Combine(root, "Control", "Resources");
            Directory.CreateDirectory(directory);
            var neutralPath = Path.Combine(directory, ErrorTableFile);
            WriteTable(neutralPath, ("ControlClean", "A clean sentence."));
            WriteTable(Path.Combine(directory, "ErrorMessages.ru.resx"), ("ControlClean", "Clean."));
            WriteTable(Path.Combine(directory, "ErrorMessages.hy.resx"), ("ControlDashed", "Name \u2014 taken"));

            var offences = DashOffences(TablesOf([neutralPath], root));

            var offence = Assert.Single(offences);
            Assert.Contains("Control/Resources/ErrorMessages.resx", offence, StringComparison.Ordinal);
            Assert.Contains("[hy]", offence, StringComparison.Ordinal);
            Assert.Contains("ControlDashed", offence, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Lists every sentence in <paramref name="tables"/> that contains a forbidden dash.</summary>
    /// <param name="tables">The tables to judge.</param>
    /// <returns>One line per offending sentence, naming table, language and key.</returns>
    private static List<string> DashOffences(IEnumerable<TableText> tables)
    {
        return tables
            .SelectMany(table =>
            {
                return table.Entries
                    .Where(entry =>
                    {
                        return entry.Value.IndexOfAny(ForbiddenDashes) >= 0;
                    })
                    .Select(entry =>
                    {
                        return $"  {table.Table} [{table.Language}] {entry.Key}";
                    });
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads every authored <c>ErrorMessages</c> table in all three languages.</summary>
    /// <returns>One entry per table and language; a missing translated file reads as empty.</returns>
    private static List<TableText> ShippedTables()
    {
        return TablesOf(ResourceTriples.NeutralPaths(), Path.Combine(ResourceTriples.BackendRoot(), "src"));
    }

    /// <summary>Reads the <c>ErrorMessages</c> tables among the neutral paths in all three languages.</summary>
    /// <param name="neutralPaths">Absolute paths of neutral resource files; other file names are skipped.</param>
    /// <param name="sourceRoot">The folder the table labels are made relative to.</param>
    /// <returns>One entry per table and language; a missing translated file reads as empty.</returns>
    private static List<TableText> TablesOf(IEnumerable<string> neutralPaths, string sourceRoot)
    {
        var tables = new List<TableText>();
        foreach (var neutralPath in neutralPaths)
        {
            if (!string.Equals(Path.GetFileName(neutralPath), ErrorTableFile, StringComparison.Ordinal))
            {
                continue;
            }

            var label = Path.GetRelativePath(sourceRoot, neutralPath).Replace(Path.DirectorySeparatorChar, '/');
            foreach (var language in Languages)
            {
                var path = language is null ? neutralPath : ResourceTriples.TranslatedPathOf(neutralPath, language);
                tables.Add(new TableText(label, language ?? "en", ResourceTriples.Read(path)));
            }
        }

        return tables;
    }

    /// <summary>Writes a resource file holding the given entries, in the shape the shipped files have.</summary>
    /// <param name="path">Absolute path of the file to write.</param>
    /// <param name="pairs">The keys and their sentences.</param>
    private static void WriteTable(string path, params (string Key, string Text)[] pairs)
    {
        var root = new XElement("root");
        foreach (var (key, text) in pairs)
        {
            root.Add(new XElement("data", new XAttribute("name", key), new XElement("value", text)));
        }

        new XDocument(root).Save(path);
    }

    /// <summary>Builds a synthetic table body from key and sentence pairs.</summary>
    /// <param name="pairs">The keys and their sentences.</param>
    /// <returns>The entries, keyed the way <see cref="ResourceTriples.Read"/> returns them.</returns>
    private static Dictionary<string, string> Entries(params (string Key, string Text)[] pairs)
    {
        return pairs.ToDictionary(
            pair =>
            {
                return pair.Key;
            },
            pair =>
            {
                return pair.Text;
            },
            StringComparer.Ordinal);
    }

    /// <summary>One language's file of one table, as authored.</summary>
    /// <param name="Table">The table's path relative to <c>backend/src</c>.</param>
    /// <param name="Language">The language code, <c>en</c> for the neutral file.</param>
    /// <param name="Entries">Key to sentence.</param>
    private sealed record TableText(string Table, string Language, IReadOnlyDictionary<string, string> Entries);
}
