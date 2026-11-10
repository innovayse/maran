using Maran.Modules.Dns.Commands.SaveZoneTemplate;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Queries.ListZoneTemplates;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Dns.Tests.Validators;

/// <summary>
/// The boundary refuses a malformed template with the code of the field that is wrong, as a bare resx key, and
/// accepts the nearest valid one: the validators mirror the handler's per-record law.
/// </summary>
public sealed class ZoneTemplateValidatorTests
{
    /// <summary>Builds a command with one record.</summary>
    /// <param name="name">The template's name.</param>
    /// <param name="record">The record.</param>
    /// <returns>The command.</returns>
    private static SaveZoneTemplateCommand Command(string name, TemplateRecordDraft record)
    {
        return new SaveZoneTemplateCommand(name, "about", [record]);
    }

    /// <summary>A malformed record or name fails with the key of what is wrong.</summary>
    [Theory]
    [InlineData("ok", "@", RecordType.Txt, "{nope}", "ZoneTemplateTokenUnknown")]
    [InlineData("ok", "@", RecordType.Txt, "{apex", "ZoneTemplateTokenUnbalanced")]
    [InlineData("ok", "@", RecordType.A, "{apex}", "ZoneTemplateTokenNotAllowed")]
    [InlineData("ok", "@", RecordType.Txt, "a\nb", "RecordValueInvalid")]
    [InlineData("ok", "{apex}", RecordType.A, "{ipv4}", "RecordNameInvalid")]
    [InlineData("", "@", RecordType.A, "{ipv4}", "ZoneTemplateNameInvalid")]
    [InlineData("a\tb", "@", RecordType.A, "{ipv4}", "ZoneTemplateNameInvalid")]
    public void A_malformed_template_fails_with_the_code_of_its_field(
        string name,
        string recordName,
        RecordType type,
        string value,
        string code)
    {
        var result = new SaveZoneTemplateCommandValidator().Validate(
            Command(name, new TemplateRecordDraft(recordName, type, null, value)));

        Assert.Equal(code, result.Errors.First().ErrorMessage);
    }

    /// <summary>Inverse control: a well-formed template passes, tokens and all.</summary>
    [Fact]
    public void A_well_formed_template_passes()
    {
        var result = new SaveZoneTemplateCommandValidator().Validate(Command(
            "Starter", new TemplateRecordDraft("@", RecordType.Mx, null, "mail.{apex}", Priority: 10)));

        Assert.True(result.IsValid);
    }

    /// <summary>A description that is too long is refused; an empty one passes.</summary>
    [Fact]
    public void A_description_is_bounded()
    {
        var validator = new SaveZoneTemplateCommandValidator();
        var record = new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}");

        var tooLong = validator.Validate(new SaveZoneTemplateCommand("ok", new string('a', 257), [record]));
        var empty = validator.Validate(new SaveZoneTemplateCommand("ok", string.Empty, [record]));

        Assert.Equal("ZoneTemplateDescriptionInvalid", tooLong.Errors.First().ErrorMessage);
        Assert.True(empty.IsValid);
    }

    /// <summary>The list takes the paging bounds and only the sorts it declares.</summary>
    [Fact]
    public void The_list_query_is_held_to_its_sort_whitelist()
    {
        var validator = new ListZoneTemplatesQueryValidator();

        Assert.True(validator.Validate(new ListZoneTemplatesQuery(Sort: SortRequest.Parse("-updatedAt"))).IsValid);
        Assert.False(validator.Validate(new ListZoneTemplatesQuery(Sort: SortRequest.Parse("description"))).IsValid);
    }
}
