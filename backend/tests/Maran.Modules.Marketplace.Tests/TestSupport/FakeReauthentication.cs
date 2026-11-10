using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// A password check that accepts one password and refuses every other, counting the refusals the way the
/// real one counts them toward lockout.
/// </summary>
public sealed class FakeReauthentication : IReauthentication
{
    /// <summary>The one password the check accepts.</summary>
    public const string ValidPassword = "correct horse";

    /// <summary>The refusal the real check gives for every failure.</summary>
    public static readonly Error Refused = Error.Of("ReauthenticationFailed", ErrorType.Validation);

    /// <summary>How many times the check refused a password.</summary>
    public int Refusals { get; private set; }

    /// <summary>How many times the check was asked.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task<Result> VerifyAsync(Guid userId, string password, CancellationToken ct)
    {
        Calls++;

        if (password == ValidPassword)
        {
            return Task.FromResult(Result.Ok());
        }

        Refusals++;
        return Task.FromResult(Result.Fail(Refused));
    }
}
