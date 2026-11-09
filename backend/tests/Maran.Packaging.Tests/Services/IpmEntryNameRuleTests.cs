using Maran.Packaging.Services;
using Maran.Packaging.Tests.Builders;

namespace Maran.Packaging.Tests.Services;

/// <summary>What a name inside a package may be: contained, relative, and from a short allow-list of characters.</summary>
public sealed class IpmEntryNameRuleTests
{
    /// <summary>Names inside the format's layout are allowed.</summary>
    /// <param name="name">A name that must be accepted.</param>
    [Theory]
    [InlineData("ipm.json")]
    [InlineData("lib/Hello.dll")]
    [InlineData("lib.enc")]
    [InlineData("spa/locales/en.json")]
    [InlineData("spa/assets/app-1.2_b.js")]
    public void A_contained_name_from_the_allow_list_is_accepted(string name)
    {
        Assert.Null(IpmEntryNameRule.Judge(name));
    }

    /// <summary>Every spelling of leaving the package folder is a traversal.</summary>
    /// <param name="name">A name that must be refused as a traversal.</param>
    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("lib/../../evil.dll")]
    [InlineData("lib/..")]
    [InlineData("./lib/a.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("lib\\..\\evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    public void A_name_that_leaves_the_package_folder_is_refused_as_a_traversal(string name)
    {
        RefusalAssert.Is("IpmEntryTraversal", IpmEntryNameRule.Judge(name));
    }

    /// <summary>A name outside the allow-list is refused, whatever it would have done.</summary>
    /// <param name="name">A name that must be refused as not allowed.</param>
    [Theory]
    [InlineData("")]
    [InlineData("lib//a.dll")]
    [InlineData("lib/")]
    [InlineData("lib/a b.dll")]
    [InlineData("lib/caf\u00e9.dll")]
    [InlineData("lib/a\u0000.dll")]
    [InlineData("lib/a\nb.dll")]
    public void A_name_outside_the_allow_list_is_refused(string name)
    {
        RefusalAssert.Is("IpmEntryNameInvalid", IpmEntryNameRule.Judge(name));
    }

    /// <summary>Null, an over-long name and an over-deep path are refused.</summary>
    [Fact]
    public void Null_an_over_long_name_and_an_over_deep_path_are_refused()
    {
        RefusalAssert.Is("IpmEntryNameInvalid", IpmEntryNameRule.Judge(null));
        RefusalAssert.Is("IpmEntryNameInvalid", IpmEntryNameRule.Judge(new string('a', 256)));
        RefusalAssert.Is("IpmEntryNameInvalid", IpmEntryNameRule.Judge(string.Join('/', Enumerable.Repeat("a", 17))));
        Assert.Null(IpmEntryNameRule.Judge(string.Join('/', Enumerable.Repeat("a", 16))));
        Assert.Null(IpmEntryNameRule.Judge(new string('a', 255)));
    }
}
