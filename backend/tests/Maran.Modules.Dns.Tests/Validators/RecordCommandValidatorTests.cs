using Maran.Modules.Dns.Commands.AddRecord;
using Maran.Modules.Dns.Commands.ReplaceRecords;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Validators;

/// <summary>The boundary refuses a malformed record with the code of the field that is wrong, as a bare resx key.</summary>
public sealed class RecordCommandValidatorTests
{
    /// <summary>A malformed record fails with the key of the first wrong field.</summary>
    [Theory]
    [InlineData("www", RecordType.A, 300, "nope", "RecordValueInvalid")]
    [InlineData("w w", RecordType.A, 300, "203.0.113.7", "RecordNameInvalid")]
    [InlineData("www", RecordType.A, 1, "203.0.113.7", "RecordTtlOutOfRange")]
    [InlineData("www", RecordType.Txt, 300, "a\"b", "RecordValueInvalid")]
    public void A_malformed_record_fails_with_the_code_of_its_field(
        string name,
        RecordType type,
        int ttl,
        string value,
        string code)
    {
        var result = new AddRecordCommandValidator().Validate(new AddRecordCommand(new RecordDraft(name, type, ttl, value)));

        Assert.Equal(code, result.Errors.First().ErrorMessage);
    }

    /// <summary>A well-formed record passes.</summary>
    [Fact]
    public void A_well_formed_record_passes()
    {
        var result = new AddRecordCommandValidator().Validate(
            new AddRecordCommand(new RecordDraft("www", RecordType.A, 300, "203.0.113.7")));

        Assert.True(result.IsValid);
    }

    /// <summary>An import is held to the same law record by record, and to a bound on its length.</summary>
    [Fact]
    public void An_import_is_held_to_the_same_law_and_a_bound()
    {
        var validator = new ReplaceRecordsCommandValidator();
        var good = new RecordDraft("a", RecordType.A, 300, "203.0.113.7");
        var bad = new RecordDraft("b", RecordType.A, 300, "nope");
        var tooMany = Enumerable.Repeat(good, 10_001).ToList();

        Assert.True(validator.Validate(new ReplaceRecordsCommand([good])).IsValid);
        Assert.False(validator.Validate(new ReplaceRecordsCommand([good, bad])).IsValid);
        Assert.False(validator.Validate(new ReplaceRecordsCommand(tooMany)).IsValid);
    }
}
