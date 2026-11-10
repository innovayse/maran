using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>What a template may be called and described as, and the spelling two names are compared in.</summary>
public sealed class TemplateNamePolicyTests
{
    /// <summary>A name of one to sixty-four characters without a control character is acceptable; others are not.</summary>
    [Theory]
    [InlineData("web", true)]
    [InlineData("web and mail", true)]
    [InlineData("", false)]
    [InlineData("tab\there", false)]
    [InlineData("new\nline", false)]
    public void A_name_is_valid_when_it_is_short_text_without_control_characters(string name, bool valid)
    {
        Assert.Equal(valid, TemplateNamePolicy.IsNameValid(TemplateNamePolicy.Normalize(name)));
    }

    /// <summary>The longest name is accepted and one more character is not.</summary>
    [Fact]
    public void The_name_length_bound_is_exact()
    {
        Assert.True(TemplateNamePolicy.IsNameValid(new string('a', TemplateNamePolicy.MaxNameLength)));
        Assert.False(TemplateNamePolicy.IsNameValid(new string('a', TemplateNamePolicy.MaxNameLength + 1)));
    }

    /// <summary>A description may be empty, is bounded, and carries no control character.</summary>
    [Fact]
    public void A_description_is_bounded_and_free_of_control_characters()
    {
        Assert.True(TemplateNamePolicy.IsDescriptionValid(string.Empty));
        Assert.True(TemplateNamePolicy.IsDescriptionValid(new string('a', TemplateNamePolicy.MaxDescriptionLength)));
        Assert.False(TemplateNamePolicy.IsDescriptionValid(new string('a', TemplateNamePolicy.MaxDescriptionLength + 1)));
        Assert.False(TemplateNamePolicy.IsDescriptionValid("a\nb"));
    }

    /// <summary>Two names that differ in case or surrounding space compare as the same.</summary>
    [Fact]
    public void Names_are_compared_without_case_or_surrounding_space()
    {
        Assert.Equal(TemplateNamePolicy.Key("Web"), TemplateNamePolicy.Key("  web "));
        Assert.NotEqual(TemplateNamePolicy.Key("web"), TemplateNamePolicy.Key("web and mail"));
    }
}
