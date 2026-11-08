using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.Demo.Tests.Fixtures;

/// <summary>A current user who is an administrator, for the journal's actor.</summary>
public sealed class AdministratorUser : ICurrentUser
{
    /// <summary>The administrator.</summary>
    public Caller Caller { get; } = new AdministratorCaller(
        new Guid("00000000-0000-4000-8000-0000000000d1"),
        "demo-admin");
}
