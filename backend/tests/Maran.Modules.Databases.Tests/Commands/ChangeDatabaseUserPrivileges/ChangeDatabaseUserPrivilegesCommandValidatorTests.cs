using Maran.Modules.Databases.Commands.ChangeDatabaseUserPrivileges;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Domain.Policies;

namespace Maran.Modules.Databases.Tests.Commands.ChangeDatabaseUserPrivileges;

/// <summary>The closed privilege list a request is held to before it reaches the handler.</summary>
public sealed class ChangeDatabaseUserPrivilegesCommandValidatorTests
{
    private static readonly ChangeDatabaseUserPrivilegesCommandValidator Validator = new();

    /// <summary>An empty list is valid because it removes the grant.</summary>
    [Fact]
    public void An_empty_list_is_valid_because_it_removes_the_grant()
    {
        Assert.True(Validator.Validate(new ChangeDatabaseUserPrivilegesCommand([])).IsValid);
    }

    /// <summary>Every privilege at once is valid.</summary>
    [Fact]
    public void Every_privilege_at_once_is_valid()
    {
        Assert.True(Validator.Validate(new ChangeDatabaseUserPrivilegesCommand(DatabasePrivilegeSets.All)).IsValid);
    }

    /// <summary>A repeated privilege is refused with the invalid code.</summary>
    [Fact]
    public void A_repeated_privilege_is_refused_with_the_invalid_code()
    {
        var result = Validator.Validate(
            new ChangeDatabaseUserPrivilegesCommand([DatabasePrivilege.Select, DatabasePrivilege.Select]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => { return error.ErrorMessage == "DatabasePrivilegesInvalid"; });
    }

    /// <summary>A number the list does not define is refused, which is how GRANT OPTION and FILE stay unnameable.</summary>
    [Theory]
    [InlineData(18)]
    [InlineData(-1)]
    [InlineData(99)]
    public void A_number_the_list_does_not_define_is_refused(int number)
    {
        var result = Validator.Validate(new ChangeDatabaseUserPrivilegesCommand([(DatabasePrivilege)number]));

        Assert.False(result.IsValid);
    }

    /// <summary>A missing list is refused rather than treated as an empty one.</summary>
    [Fact]
    public void A_missing_list_is_refused_rather_than_treated_as_an_empty_one()
    {
        Assert.False(Validator.Validate(new ChangeDatabaseUserPrivilegesCommand(null!)).IsValid);
    }
}
