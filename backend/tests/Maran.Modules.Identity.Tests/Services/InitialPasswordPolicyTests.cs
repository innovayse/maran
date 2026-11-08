using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of <see cref="InitialPasswordPolicy"/>, the one verdict on a password an
/// administrator chose for an account owner. Each refusal is paired with the acceptance on its other
/// side, so a policy that refused everything would fail here.
/// </summary>
public sealed class InitialPasswordPolicyTests : IDisposable
{
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private InitialPasswordPolicy Policy()
    {
        return new InitialPasswordPolicy(TestSecurityPolicyCache.Over(_context));
    }

    /// <summary>A password one character under the operator's minimum is refused.</summary>
    [Fact]
    public async Task A_password_shorter_than_the_minimum_is_refused()
    {
        var tooShort = new SensitiveString(new string('x', SecurityPolicy.DefaultMinimumPasswordLength - 1));

        Assert.False(await Policy().AcceptsAsync(tooShort, "owner-one", CancellationToken.None));
    }

    /// <summary>A password exactly as long as the minimum is accepted: the boundary from the other side.</summary>
    [Fact]
    public async Task A_password_exactly_as_long_as_the_minimum_is_accepted()
    {
        var exact = new SensitiveString(new string('x', SecurityPolicy.DefaultMinimumPasswordLength));

        Assert.True(await Policy().AcceptsAsync(exact, "owner-one", CancellationToken.None));
    }

    /// <summary>The operator's own minimum is read, not the built-in default.</summary>
    [Fact]
    public async Task The_operators_stricter_minimum_is_the_one_applied()
    {
        var stricter = SecurityPolicy.DefaultMinimumPasswordLength + 6;
        var policy = new InitialPasswordPolicy(TestSecurityPolicyCache.Saved(_context, minimumPasswordLength: stricter));

        Assert.False(await policy.AcceptsAsync(
            new SensitiveString(new string('x', stricter - 1)), "owner-one", CancellationToken.None));
        Assert.True(await policy.AcceptsAsync(
            new SensitiveString(new string('x', stricter)), "owner-one", CancellationToken.None));
    }

    /// <summary>A password equal to the login name is refused.</summary>
    [Fact]
    public async Task A_password_equal_to_the_username_is_refused()
    {
        var username = new string('u', SecurityPolicy.DefaultMinimumPasswordLength);

        Assert.False(await Policy().AcceptsAsync(new SensitiveString(username), username, CancellationToken.None));
    }

    /// <summary>A password over the ceiling is refused and one at the ceiling is accepted.</summary>
    [Fact]
    public async Task A_password_over_the_ceiling_is_refused()
    {
        Assert.False(await Policy().AcceptsAsync(
            new SensitiveString(new string('x', PasswordPolicy.MaxLength + 1)), "owner-one", CancellationToken.None));
        Assert.True(await Policy().AcceptsAsync(
            new SensitiveString(new string('x', PasswordPolicy.MaxLength)), "owner-one", CancellationToken.None));
    }
}
