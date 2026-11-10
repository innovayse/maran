using Maran.Modules.Marketplace.Domain.Policies;

namespace Maran.Modules.Marketplace.Tests.Domain.Policies;

/// <summary>A module id is plain before it reaches a URL, a lock, a task or an audit entry.</summary>
public sealed class ModuleIdPolicyTests
{
    /// <summary>The longest id passes and one character more does not.</summary>
    [Fact]
    public void The_length_limit_is_inclusive()
    {
        Assert.True(ModuleIdPolicy.IsValid(new string('a', ModuleIdPolicy.MaxLength)));
        Assert.False(ModuleIdPolicy.IsValid(new string('a', ModuleIdPolicy.MaxLength + 1)));
    }

    /// <summary>Null, empty, digit-first, upper-case, punctuated and non-ASCII ids are refused; a plain one is accepted.</summary>
    /// <param name="id">The id.</param>
    /// <param name="valid">Whether it is acceptable.</param>
    [Theory]
    [InlineData("mail", true)]
    [InlineData("a1", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("1a", false)]
    [InlineData("Mail", false)]
    [InlineData("ma.il", false)]
    [InlineData("poșta", false)]
    public void Only_plain_ids_are_accepted(string? id, bool valid)
    {
        Assert.Equal(valid, ModuleIdPolicy.IsValid(id));
    }
}
