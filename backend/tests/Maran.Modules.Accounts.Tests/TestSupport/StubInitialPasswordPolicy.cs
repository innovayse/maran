using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="IInitialPasswordPolicy"/> double: it accepts every password except those it was told to
/// refuse, each with the verdict it was told to give, and records the login name each question was asked for.
/// </summary>
public sealed class StubInitialPasswordPolicy : IInitialPasswordPolicy
{
    /// <summary>The passwords this double refuses, with the rule each breaks; every other is accepted.</summary>
    private readonly Dictionary<string, InitialPasswordVerdict> _refused;

    /// <summary>Creates a double that refuses exactly <paramref name="refused"/>, each as too short.</summary>
    /// <param name="refused">The passwords to refuse; none by default.</param>
    public StubInitialPasswordPolicy(params string[] refused)
    {
        _refused = refused.ToDictionary(password => { return password; }, _ => { return InitialPasswordVerdict.TooShort; });
    }

    /// <summary>Creates a double that refuses <paramref name="password"/> for breaking <paramref name="verdict"/>.</summary>
    /// <param name="password">The password to refuse.</param>
    /// <param name="verdict">The rule it breaks.</param>
    public StubInitialPasswordPolicy(string password, InitialPasswordVerdict verdict)
    {
        _refused = new Dictionary<string, InitialPasswordVerdict> { [password] = verdict };
    }

    /// <summary>The login name of every question asked, in order.</summary>
    public List<string> AskedFor { get; } = [];

    /// <inheritdoc />
    public Task<InitialPasswordVerdict> JudgeAsync(
        SensitiveString password,
        string username,
        CancellationToken cancellationToken)
    {
        AskedFor.Add(username);

        return Task.FromResult(
            _refused.TryGetValue(password.Reveal(), out var verdict) ? verdict : InitialPasswordVerdict.Accepted);
    }
}
