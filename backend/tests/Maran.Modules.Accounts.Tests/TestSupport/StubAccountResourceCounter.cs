using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>A counter that answers one fixed count, and records which accounts it was asked about.</summary>
public sealed class StubAccountResourceCounter : IAccountResourceCounter
{
    /// <summary>The answer every call returns.</summary>
    private readonly AccountResourceCount _answer;

    /// <summary>The accounts this counter was asked about, in order.</summary>
    public List<Guid> Asked { get; } = [];

    /// <inheritdoc />
    public AccountResourceKind Kind
    {
        get
        {
            return _answer.Kind;
        }
    }

    /// <summary>Creates the counter.</summary>
    /// <param name="answer">The answer every call returns; its kind is the counter's kind.</param>
    public StubAccountResourceCounter(AccountResourceCount answer)
    {
        _answer = answer;
    }

    /// <inheritdoc />
    public Task<AccountResourceCount> CountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Asked.Add(accountId);
        return Task.FromResult(_answer);
    }
}
