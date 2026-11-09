using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioral contract of <see cref="ResellerLoginChangeRequestedHandler"/>: the reseller's login
/// follows a new name and address unless another login already holds either, and a reseller with no
/// login is a success with nothing to move.
/// </summary>
public sealed class ResellerLoginChangeRequestedHandlerTests : IDisposable
{
    /// <summary>The reseller whose login moves.</summary>
    private static readonly Guid ResellerId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    /// <summary>The fixed instant logins are created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The module's context, in memory.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Stores the reseller's login.</summary>
    private async Task<User> SeedResellerLoginAsync()
    {
        var user = User.InviteReseller(Guid.NewGuid(), "acme", "old@example.com", ResellerId, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>Stores another login with the given name and address.</summary>
    /// <param name="username">The login name.</param>
    /// <param name="email">The login's address.</param>
    private async Task SeedOtherLoginAsync(string username, string email)
    {
        _context.Users.Add(User.Invite(Guid.NewGuid(), username, email, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Asks the handler to move the login.</summary>
    /// <param name="username">The new name.</param>
    /// <param name="email">The new address.</param>
    private Task<Result> RunAsync(string username, string email)
    {
        return new ResellerLoginChangeRequestedHandler(_context)
            .HandleAsync(new ResellerLoginChangeRequested(ResellerId, username, email), CancellationToken.None);
    }

    /// <summary>The reseller's login moves to the new name and address.</summary>
    [Fact]
    public async Task The_resellers_login_moves_to_the_new_name_and_address()
    {
        var user = await SeedResellerLoginAsync();

        var result = await RunAsync("globex", "new@example.com");

        Assert.True(result.IsSuccess);
        var stored = await _context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("globex", stored.Username);
        Assert.Equal("new@example.com", stored.Email);
        Assert.Equal(ResellerId, stored.ResellerId);
    }

    /// <summary>A name another login holds is refused and the login keeps its name and address.</summary>
    [Fact]
    public async Task A_name_another_login_holds_is_refused_and_the_login_keeps_its_name_and_address()
    {
        var user = await SeedResellerLoginAsync();
        await SeedOtherLoginAsync("globex", "other@example.com");

        var result = await RunAsync("globex", "new@example.com");

        Assert.Equal("UsernameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var stored = await _context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("acme", stored.Username);
        Assert.Equal("old@example.com", stored.Email);
    }

    /// <summary>An address another login holds is refused and the login keeps its name and address.</summary>
    [Fact]
    public async Task An_address_another_login_holds_is_refused_and_the_login_keeps_its_name_and_address()
    {
        var user = await SeedResellerLoginAsync();
        await SeedOtherLoginAsync("someone", "taken@example.com");

        var result = await RunAsync("globex", "taken@example.com");

        Assert.Equal("EmailTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var stored = await _context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("acme", stored.Username);
        Assert.Equal("old@example.com", stored.Email);
    }

    /// <summary>Keeping the name and the address the login already has is accepted.</summary>
    [Fact]
    public async Task Keeping_the_name_and_the_address_the_login_already_has_is_accepted()
    {
        await SeedResellerLoginAsync();

        var result = await RunAsync("acme", "old@example.com");

        Assert.True(result.IsSuccess);
    }

    /// <summary>A reseller with no login is a success with nothing to move.</summary>
    [Fact]
    public async Task A_reseller_with_no_login_is_a_success_with_nothing_to_move()
    {
        var result = await RunAsync("globex", "new@example.com");

        Assert.True(result.IsSuccess);
        Assert.Empty(await _context.Users.ToListAsync());
    }
}
