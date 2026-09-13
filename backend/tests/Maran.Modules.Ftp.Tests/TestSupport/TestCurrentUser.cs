using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// The administrator every attended test acts as: the module's endpoints are <c>AdminOnly</c>, so a
/// customer principal could never have reached the handlers under test.
/// </summary>
public sealed class TestCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid UserId { get; } = new("2f7a9f2a-6a1e-4f5a-9c1f-0b6c5a2e7d10");

    /// <inheritdoc />
    public string Username
    {
        get
        {
            return "admin";
        }
    }

    /// <inheritdoc />
    public Guid? AccountId
    {
        get
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAdmin
    {
        get
        {
            return true;
        }
    }
}
