namespace Maran.Sdk.Contracts;

/// <summary>
/// The key was new: the store has reserved it for this request, which must now run and then call
/// <c>CompleteAsync</c> with its answer, or <c>AbandonAsync</c> when it produced nothing worth keeping,
/// passing <paramref name="Token"/> back each time.
/// </summary>
/// <param name="Token">
/// Minted when the reservation was made and held by this request alone. It is what lets the store tell
/// this request's reservation from a later one on the same key: a request that outlives its lease and
/// finds the key re-reserved by another request holds a token that no longer matches, so it can neither
/// complete nor release the other request's row.
/// </param>
public sealed record IdempotencyStarted(Guid Token) : IdempotencyBegin;
