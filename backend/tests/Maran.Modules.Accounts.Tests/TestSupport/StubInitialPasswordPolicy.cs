using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="IInitialPasswordPolicy"/> double: it accepts every password except those it was told to
/// refuse, and records the login name each question was asked for.
/// </summary>
public sealed class StubInitialPasswordPolicy : IInitialPasswordPolicy
{
    /// <summary>The passwords this double refuses; every other is accepted.</summary>
    private readonly HashSet<string> _refused;

    /// <summary>Creates a double that refuses exactly <paramref name="refused"/>.</summary>
    /// <param name="refused">The passwords to refuse; none by default.</param>
    public StubInitialPasswordPolicy(params string[] refused)
    {
        _refused = [.. refused];
    }

    /// <summary>The login name of every question asked, in order.</summary>
    public List<string> AskedFor { get; } = [];

    /// <inheritdoc />
    public Task<bool> AcceptsAsync(SensitiveString password, string username, CancellationToken cancellationToken)
    {
        AskedFor.Add(username);

        return Task.FromResult(!_refused.Contains(password.Reveal()));
    }
}
