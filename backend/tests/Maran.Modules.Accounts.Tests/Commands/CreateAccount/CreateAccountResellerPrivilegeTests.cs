using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// The two options that give the creator a way into the customer's account, a login shell and a first
/// password the creator chose, are the administrator's alone (issue #488): a reseller, and an API key a
/// reseller owns, is refused them before anything is provisioned, and the administrator is not.
/// </summary>
public sealed class CreateAccountResellerPrivilegeTests : IDisposable
{
    /// <summary>The agent double.</summary>
    private readonly RecordingAgentAccountsClient _agent = new();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The world the current test built; disposed with the class.</summary>
    private readonly ResellerAccountsWorld _world = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _world.Dispose();
    }

    /// <summary>Creates account <c>fresh</c> for the first reseller as <paramref name="caller"/>.</summary>
    /// <param name="caller">Who creates it.</param>
    /// <param name="shellAccess">Whether a login shell is asked for.</param>
    /// <param name="initialPassword">The owner's first password, or <c>null</c> for the invitation.</param>
    /// <returns>The handler's result.</returns>
    private async Task<Result<AccountDto>> CreateAsync(
        FakeCurrentUser caller,
        bool shellAccess = false,
        string? initialPassword = null)
    {
        var handler = AccountHandlerFactory.Create(_world.ContextFor(caller), caller, _agent, _audit);

        return await handler.HandleAsync(
            new CreateAccountCommand(
                "fresh",
                "fresh.example.com",
                _world.PlanA.Id,
                "owner@example.com",
                ResellerId: _world.ResellerA.Id,
                IpAddress: "203.0.113.7",
                UserAgent: "unit-tests",
                LoginMode: initialPassword is null ? OwnerLoginMode.Invitation : OwnerLoginMode.InitialPassword,
                InitialPassword: initialPassword is null ? null : new SensitiveString(initialPassword),
                ShellAccess: shellAccess),
            CancellationToken.None);
    }

    /// <summary>A reseller asking for a login shell is refused, nothing is provisioned, and the refusal is journalled.</summary>
    [Fact]
    public async Task A_reseller_asking_for_a_login_shell_is_refused_before_anything_is_provisioned()
    {
        var result = await CreateAsync(_world.UserA, shellAccess: true);

        Assert.Equal("AccountShellAccessAdministratorOnly", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Empty(_agent.Calls);
        Assert.False(Assert.Single(_audit.Entries).Succeeded);
    }

    /// <summary>A reseller choosing the owner's first password is refused and nothing is provisioned.</summary>
    [Fact]
    public async Task A_reseller_choosing_the_owners_first_password_is_refused_before_anything_is_provisioned()
    {
        var result = await CreateAsync(_world.UserA, initialPassword: "correct horse battery staple");

        Assert.Equal("AccountInitialPasswordAdministratorOnly", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>An API key a reseller owns is a reseller here, so it is refused a shell as well.</summary>
    [Fact]
    public async Task An_api_key_a_reseller_owns_is_refused_a_login_shell()
    {
        var result = await CreateAsync(FakeCurrentUser.ApiKey(_world.ResellerA.Id), shellAccess: true);

        Assert.Equal("AccountShellAccessAdministratorOnly", result.Error!.Code);
        Assert.Empty(_agent.Calls);
    }

    /// <summary>The accepting control: a reseller creating with the invitation and no shell is not refused.</summary>
    [Fact]
    public async Task A_reseller_creating_with_the_invitation_and_no_shell_is_accepted()
    {
        var result = await CreateAsync(_world.UserA);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(_agent.Calls);
    }

    /// <summary>The accepting control: an administrator may grant a shell and state a first password.</summary>
    [Fact]
    public async Task An_administrator_may_grant_a_login_shell_and_state_a_first_password()
    {
        var result = await CreateAsync(
            FakeCurrentUser.Admin(), shellAccess: true, initialPassword: "correct horse battery staple");

        Assert.True(result.IsSuccess);
    }
}
