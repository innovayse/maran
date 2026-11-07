using Maran.SharedKernel.Utilities.Text;

namespace Maran.SharedKernel.Tests.Utilities.Text;

/// <summary>Behavioural contract of <see cref="CsvField"/>.</summary>
public sealed class CsvFieldTests
{
    /// <summary>A plain value is written unchanged.</summary>
    [Fact]
    public void A_plain_value_is_written_unchanged()
    {
        Assert.Equal("LoginSucceeded", CsvField.Of("LoginSucceeded"));
    }

    /// <summary>A missing value and an empty one are both an empty field.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_or_empty_value_is_an_empty_field(string? value)
    {
        Assert.Equal(string.Empty, CsvField.Of(value));
    }

    /// <summary>A value holding a comma is wrapped in quotes.</summary>
    [Fact]
    public void A_value_with_a_comma_is_quoted()
    {
        Assert.Equal("\"a,b\"", CsvField.Of("a,b"));
    }

    /// <summary>A value holding a double quote is quoted and the quote doubled.</summary>
    [Fact]
    public void A_value_with_a_double_quote_is_quoted_and_the_quote_doubled()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", CsvField.Of("say \"hi\""));
    }

    /// <summary>A value holding a line break is quoted so it stays one field.</summary>
    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void A_value_with_a_line_break_is_quoted(string value)
    {
        Assert.Equal("\"" + value + "\"", CsvField.Of(value));
    }

    /// <summary>A value beginning like a spreadsheet formula gets a leading apostrophe.</summary>
    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tcell", "'\tcell")]
    public void A_value_that_begins_like_a_formula_gets_a_leading_apostrophe(string value, string expected)
    {
        Assert.Equal(expected, CsvField.Of(value));
    }

    /// <summary>A value beginning with a carriage return is neutralised and then quoted.</summary>
    [Fact]
    public void A_value_that_begins_with_a_carriage_return_is_neutralised_then_quoted()
    {
        Assert.Equal("\"'\rx\"", CsvField.Of("\rx"));
    }

    /// <summary>A hostile value that needs both treatments gets the apostrophe inside the quotes.</summary>
    [Fact]
    public void A_formula_with_a_comma_is_neutralised_inside_the_quotes()
    {
        Assert.Equal("\"'=HYPERLINK(\"\"http://x\"\",\"\"y\"\")\"", CsvField.Of("=HYPERLINK(\"http://x\",\"y\")"));
    }

    /// <summary>A formula leader that is not the first character is left alone (the inverse control).</summary>
    [Fact]
    public void A_formula_leader_in_the_middle_of_a_value_is_left_alone()
    {
        Assert.Equal("a=b+c-d@e", CsvField.Of("a=b+c-d@e"));
    }
}
