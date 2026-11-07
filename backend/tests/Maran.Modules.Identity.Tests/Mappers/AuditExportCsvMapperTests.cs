using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;

namespace Maran.Modules.Identity.Tests.Mappers;

/// <summary>
/// Behavioural contract of <see cref="AuditExportCsvMapper"/>: RFC 4180 records with CRLF, and no field
/// that a spreadsheet would read as a formula — the subject and the user agent are text an attacker typed.
/// </summary>
public sealed class AuditExportCsvMapperTests
{
    /// <summary>The header names the fourteen columns and ends with CRLF.</summary>
    [Fact]
    public void The_header_names_the_fourteen_columns_and_ends_with_crlf()
    {
        var header = AuditExportCsvMapper.Header();

        Assert.EndsWith("\r\n", header, StringComparison.Ordinal);
        Assert.Equal(
            [
                "id", "occurredAt", "actorUserId", "actorUsername", "impersonatorUsername", "action", "outcome",
                "subject", "ipAddress", "userAgent", "accountId", "correlationId", "chainIndex", "rowHash",
            ],
            header.TrimEnd('\r', '\n').Split(','));
    }

    /// <summary>A plain row is written field by field, with nulls as empty fields, and ends with CRLF.</summary>
    [Fact]
    public void A_plain_row_is_written_field_by_field_with_nulls_empty()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var line = AuditExportCsvMapper.Line(Row(id: id));

        Assert.Equal(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee,2026-08-30T12:00:00.000000Z,,olive,,SiteCreated,succeeded,"
            + "shop.example.com,203.0.113.7,Mozilla/5.0,,,,\r\n",
            line);
    }

    /// <summary>A sealed row writes its chain index and hash.</summary>
    [Fact]
    public void A_sealed_rows_chain_index_and_hash_are_written()
    {
        var line = AuditExportCsvMapper.Line(Row() with { ChainIndex = 17, RowHash = "ab12" });

        Assert.EndsWith(",17,ab12\r\n", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A subject or user agent that begins with a spreadsheet-formula character gets a leading
    /// apostrophe, for each of the six characters.
    /// </summary>
    /// <param name="leader">The first character of the hostile text.</param>
    [Theory]
    [InlineData("=")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("@")]
    [InlineData("\t")]
    [InlineData("\r")]
    public void A_hostile_subject_and_user_agent_are_neutralised_for_every_formula_character(string leader)
    {
        var hostile = leader + "HYPERLINK(\"http://x\")";

        var line = AuditExportCsvMapper.Line(Row() with { Subject = hostile, UserAgent = hostile });

        var fields = ParseRecord(line);
        Assert.Equal("'" + hostile, fields[7]);
        Assert.Equal("'" + hostile, fields[9]);
    }

    /// <summary>The same text with the formula character not first is left alone — the inverse control.</summary>
    [Fact]
    public void A_formula_character_that_is_not_first_is_left_alone()
    {
        var line = AuditExportCsvMapper.Line(Row() with { Subject = "a=b+c-d@e", UserAgent = "Mozilla -x" });

        var fields = ParseRecord(line);
        Assert.Equal("a=b+c-d@e", fields[7]);
        Assert.Equal("Mozilla -x", fields[9]);
    }

    /// <summary>A comma, a quote, a line feed and a carriage return inside a field are quoted per RFC 4180.</summary>
    [Fact]
    public void A_field_with_a_comma_a_quote_and_line_breaks_is_quoted()
    {
        var line = AuditExportCsvMapper.Line(Row() with { Subject = "say \"hi\", then\r\nleave" });

        Assert.Contains(",\"say \"\"hi\"\", then\r\nleave\",", line, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", line, StringComparison.Ordinal);
    }

    /// <summary>Builds a row with plain values.</summary>
    /// <param name="id">The row's id; a new one when omitted.</param>
    /// <returns>The row.</returns>
    private static AuditExportRow Row(Guid? id = null)
    {
        return new AuditExportRow(
            id ?? Guid.NewGuid(),
            "2026-08-30T12:00:00.000000Z",
            null,
            "olive",
            null,
            "SiteCreated",
            "succeeded",
            "shop.example.com",
            "203.0.113.7",
            "Mozilla/5.0",
            null,
            null,
            null,
            null);
    }

    /// <summary>
    /// Splits one record into its fields, honouring quotes. Only as general as these tests need: a
    /// quoted field may hold commas and doubled quotes.
    /// </summary>
    /// <param name="line">The record, ending with CRLF.</param>
    /// <returns>The fields, unquoted.</returns>
    private static List<string> ParseRecord(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var text = line[..^2];

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
