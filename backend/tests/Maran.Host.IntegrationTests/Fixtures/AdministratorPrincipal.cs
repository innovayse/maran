using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The "current user" of a test that dispatches an administrator's command over the bus with no
/// request around it: a signed-in server administrator, whose scope is the whole server. The
/// Accounts context is fail-closed, so an anonymous caller sees no account at all; a command that
/// production reaches only through an administrator's controller has to be dispatched as one.
/// </summary>
public sealed class AdministratorPrincipal : ICurrentUser
{
    /// <summary>The id of the one administrator login every instance speaks for.</summary>
    private static readonly Guid AdministratorUserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    /// <inheritdoc />
    public Caller Caller { get; } = new AdministratorCaller(AdministratorUserId, "admin");
}
