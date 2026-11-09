using Maran.Modules.Identity.Commands.SaveDashboardLayout;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Commands.SaveDashboardLayout;

/// <summary>Behavioural contract of <see cref="SaveDashboardLayoutCommandValidator"/>.</summary>
public sealed class SaveDashboardLayoutCommandValidatorTests
{
    private static SaveDashboardLayoutCommand Layout(
        IReadOnlyList<DashboardLayoutEntryDto>? widgets,
        IReadOnlyList<DashboardLayoutEntryDto>? actions)
    {
        return new SaveDashboardLayoutCommand(widgets!, actions!);
    }

    private static List<DashboardLayoutEntryDto> Entries(params string[] keys)
    {
        return keys.Select(key =>
        {
            return new DashboardLayoutEntryDto(key, Hidden: false);
        }).ToList();
    }

    private static List<string> Messages(SaveDashboardLayoutCommand command)
    {
        return new SaveDashboardLayoutCommandValidator().Validate(command).Errors
            .Select(error =>
            {
                return error.ErrorMessage;
            })
            .ToList();
    }

    /// <summary>A layout of well-shaped, distinct keys passes, an unknown widget included.</summary>
    [Fact]
    public void A_layout_of_distinct_well_shaped_keys_passes()
    {
        Assert.Empty(Messages(Layout(Entries("cpu", "mailQueue"), Entries("addSite"))));
        Assert.Empty(Messages(Layout(Entries(), Entries())));
    }

    /// <summary>A list at the ceiling passes and one entry more fails with the length code.</summary>
    [Fact]
    public void A_list_is_valid_up_to_the_ceiling_and_refused_beyond_it()
    {
        var atCeiling = Entries(Enumerable.Range(0, DashboardLayoutPolicy.MaxEntriesPerList).Select(index =>
        {
            return $"w{index}";
        }).ToArray());
        var beyond = Entries(Enumerable.Range(0, DashboardLayoutPolicy.MaxEntriesPerList + 1).Select(index =>
        {
            return $"w{index}";
        }).ToArray());

        Assert.Empty(Messages(Layout(atCeiling, Entries())));
        Assert.Equal("DashboardLayoutTooLong", Assert.Single(Messages(Layout(beyond, Entries()))));
        Assert.Equal("DashboardLayoutTooLong", Assert.Single(Messages(Layout(Entries(), beyond))));
    }

    /// <summary>A key outside the charset fails with its resource key, in either list.</summary>
    [Theory]
    [InlineData("Not A Key")]
    [InlineData("a\nb")]
    [InlineData("")]
    [InlineData("9lives")]
    public void A_key_outside_the_charset_is_refused(string key)
    {
        Assert.Equal("DashboardLayoutKeyInvalid", Assert.Single(Messages(Layout(Entries(key), Entries()))));
        Assert.Equal("DashboardLayoutKeyInvalid", Assert.Single(Messages(Layout(Entries(), Entries(key)))));
    }

    /// <summary>A key named twice in one list is refused; the same key in the other list is not a repeat.</summary>
    [Fact]
    public void A_key_repeated_in_one_list_is_refused_but_not_across_lists()
    {
        Assert.Equal("DashboardLayoutKeyRepeated", Assert.Single(Messages(Layout(Entries("cpu", "cpu"), Entries()))));
        Assert.Empty(Messages(Layout(Entries("cpu"), Entries("cpu"))));
    }

    /// <summary>A list the body left out is refused once, with its own code, and never throws.</summary>
    [Fact]
    public void A_missing_list_is_refused_without_throwing()
    {
        Assert.Equal("DashboardLayoutListMissing", Assert.Single(Messages(Layout(null, Entries()))));
        Assert.Equal("DashboardLayoutListMissing", Assert.Single(Messages(Layout(Entries(), null))));
    }
}
