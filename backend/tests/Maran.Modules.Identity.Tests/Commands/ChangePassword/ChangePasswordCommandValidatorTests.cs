using FluentValidation.TestHelper;
using Maran.Modules.Identity.Commands.ChangePassword;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Commands.ChangePassword;

/// <summary>Behavioural contract of the change-password validator.</summary>
public sealed class ChangePasswordCommandValidatorTests : IAsyncLifetime
{
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Nothing to seed; the policy row is saved per test where one is needed.</summary>
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private static ChangePasswordCommand Command(string current = "the old password", string next = "correct horse battery staple")
    {
        return new ChangePasswordCommand(current, next);
    }

    /// <summary>A new password shorter than the operator's minimum is refused as too weak.</summary>
    [Fact]
    public async Task A_new_password_shorter_than_the_operators_minimum_is_refused_as_too_weak()
    {
        var validator = new ChangePasswordCommandValidator(TestSecurityPolicyCache.Saved(_context, minimumPasswordLength: 20));

        var result = await validator.TestValidateAsync(Command(next: new string('x', 19)));

        result.ShouldHaveValidationErrorFor(c => c.NewPassword).WithErrorMessage("PasswordTooWeak");
    }

    /// <summary>A new password at the operator's minimum is accepted.</summary>
    [Fact]
    public async Task A_new_password_at_the_operators_minimum_is_accepted()
    {
        var validator = new ChangePasswordCommandValidator(TestSecurityPolicyCache.Saved(_context, minimumPasswordLength: 20));

        var result = await validator.TestValidateAsync(Command(next: new string('x', 20)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A new password longer than the cap is refused before it reaches the hasher.</summary>
    [Fact]
    public async Task A_new_password_longer_than_the_cap_is_refused_before_it_reaches_the_hasher()
    {
        var validator = new ChangePasswordCommandValidator(TestSecurityPolicyCache.Over(_context));

        var result = await validator.TestValidateAsync(Command(next: new string('x', 257)));

        result.ShouldHaveValidationErrorFor(c => c.NewPassword).WithErrorMessage("PasswordTooWeak");
    }

    /// <summary>An empty or over-long current password carries the wrong-password code.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public async Task An_empty_or_over_long_current_password_carries_the_wrong_password_code(int length)
    {
        var validator = new ChangePasswordCommandValidator(TestSecurityPolicyCache.Over(_context));

        var result = await validator.TestValidateAsync(Command(current: new string('x', length)));

        result.ShouldHaveValidationErrorFor(c => c.CurrentPassword).WithErrorMessage("CurrentPasswordIncorrect");
    }
}
