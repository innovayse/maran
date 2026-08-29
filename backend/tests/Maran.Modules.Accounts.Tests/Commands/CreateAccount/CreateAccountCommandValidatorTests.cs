using FluentValidation.TestHelper;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// Behavioral contract of <see cref="CreateAccountCommandValidator"/>. <see cref="CreateAccountCommand.Name"/>
/// becomes the account's eventual Linux user name, so its pattern is a security boundary
/// (rules/security.md "Input") and is covered exhaustively here, not just happy-path. The plan
/// existence rule runs against a real (InMemory) <see cref="AccountsDbContext"/> seeded with one
/// known plan, since that is the validator's own dependency.
/// </summary>
public sealed class CreateAccountCommandValidatorTests : IDisposable
{
    /// <summary>The id of the one plan seeded into <see cref="_dbContext"/> for every test.</summary>
    private static readonly Guid SeededPlanId = Guid.NewGuid();

    /// <summary>A fresh, isolated in-memory <see cref="AccountsDbContext"/>, seeded with one plan.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The validator under test.</summary>
    private readonly CreateAccountCommandValidator _validator;

    /// <summary>Builds the shared database double and the validator under test.</summary>
    public CreateAccountCommandValidatorTests()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AccountsDbContext(options);
        _dbContext.Plans.Add(new Plan(SeededPlanId, "PlanStarterName", 5_120, 5, 2, 3));
        _dbContext.SaveChanges();

        _validator = new CreateAccountCommandValidator(_dbContext);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Builds a command that satisfies every rule, so a single field can be broken per test.</summary>
    private static CreateAccountCommand ValidCommand()
    {
        return new("acme", "acme.example.com", SeededPlanId);
    }

    [Fact]
    public async Task Fully_valid_command_passes_every_rule()
    {
        var result = await _validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Empty_name_fails_on_the_name_property()
    {
        var command = ValidCommand() with { Name = string.Empty };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.Name);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    public async Task Name_shorter_than_three_characters_fails_on_the_name_property(string tooShort)
    {
        var command = ValidCommand() with { Name = tooShort };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.Name);
    }

    [Fact]
    public async Task Name_longer_than_thirty_two_characters_fails_on_the_name_property()
    {
        var tooLong = "a" + new string('b', 32); // 33 characters, one past the limit.
        var command = ValidCommand() with { Name = tooLong };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.Name);
    }

    [Theory]
    [InlineData("Acme")] // uppercase is not part of the portable username set
    [InlineData("1acme")] // must start with a letter, not a digit
    [InlineData("-acme")] // must start with a letter, not a hyphen
    [InlineData("acme!")] // shell-meaningful character
    [InlineData("acme user")] // whitespace
    [InlineData("acme;rm -rf /")] // command-injection-shaped payload
    [InlineData("../etc/passwd")] // path-traversal-shaped payload
    [InlineData("acme.example")] // dot is not in the portable username set
    public async Task Name_with_an_illegal_character_or_shape_fails_on_the_name_property(string illegal)
    {
        var command = ValidCommand() with { Name = illegal };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.Name);
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("acme-01")]
    [InlineData("acme_01")]
    [InlineData("a23")]
    public async Task Name_matching_the_portable_username_pattern_passes(string legal)
    {
        var command = ValidCommand() with { Name = legal };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(c => c.Name);
    }

    [Fact]
    public async Task Empty_primary_domain_fails_on_the_primary_domain_property()
    {
        var command = ValidCommand() with { PrimaryDomain = string.Empty };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.PrimaryDomain);
    }

    [Theory]
    [InlineData("not-a-domain")] // no dot, so no TLD
    [InlineData("-acme.example.com")] // leading hyphen on a label
    [InlineData("acme-.example.com")] // trailing hyphen on a label
    [InlineData("acme..com")] // empty label
    [InlineData("acme.example.com/path")] // not a bare domain
    public async Task Malformed_primary_domain_fails_on_the_primary_domain_property(string badDomain)
    {
        var command = ValidCommand() with { PrimaryDomain = badDomain };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.PrimaryDomain);
    }

    [Fact]
    public async Task Too_long_primary_domain_fails_on_the_primary_domain_property()
    {
        // Four 63-character labels (each individually within the per-label limit the pattern
        // allows) joined by dots total 255 characters, one past the rule's 253-character cap.
        var label = new string('a', 63);
        var tooLong = string.Join('.', Enumerable.Repeat(label, 4));
        var command = ValidCommand() with { PrimaryDomain = tooLong };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.PrimaryDomain);
    }

    [Fact]
    public async Task Missing_plan_id_fails_on_the_plan_id_property()
    {
        var command = ValidCommand() with { PlanId = Guid.Empty };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.PlanId);
    }

    [Fact]
    public async Task Plan_id_that_does_not_exist_fails_on_the_plan_id_property_with_the_plan_not_found_code()
    {
        var command = ValidCommand() with { PlanId = Guid.NewGuid() };

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.PlanId)
            .WithErrorCode("PlanNotFound");
    }

    [Fact]
    public async Task Plan_id_that_exists_passes_the_plan_id_rule()
    {
        var result = await _validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveValidationErrorFor(c => c.PlanId);
    }
}
