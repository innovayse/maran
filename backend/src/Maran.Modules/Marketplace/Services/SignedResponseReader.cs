using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Mappers;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Turns a marketplace answer into a value the panel may act on, in the one order that is safe: the
/// status first, then the signature over the exact bytes, and only then the JSON. A body whose signature
/// was not checked is never parsed, so a forged answer cannot reach the deserializer.
/// </summary>
public sealed class SignedResponseReader
{
    /// <summary>Verifies signatures and freshness.</summary>
    private readonly CatalogueSignatureChecker _checker;

    /// <summary>Creates the reader.</summary>
    /// <param name="checker">Verifies signatures and freshness.</param>
    public SignedResponseReader(CatalogueSignatureChecker checker)
    {
        _checker = checker;
    }

    /// <summary>Reads a signed, dated document: status, signature, shape, then freshness.</summary>
    /// <typeparam name="T">The document's shape.</typeparam>
    /// <param name="response">The answer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document, or the coded reason it cannot be believed.</returns>
    public async Task<Result<T>> ReadDocumentAsync<T>(MarketplaceResponse response, CancellationToken ct)
        where T : class, ISignedMarketplaceDocument
    {
        var read = await ReadVerifiedAsync<T>(response, ct);
        if (!read.IsSuccess)
        {
            return read;
        }

        var fresh = _checker.CheckFreshness(read.Value);
        return fresh.IsSuccess ? read : Result<T>.Fail(fresh.Error!);
    }

    /// <summary>Reads a signed answer that carries its own expiry rather than a signing date: status, signature, shape.</summary>
    /// <typeparam name="T">The answer's shape.</typeparam>
    /// <param name="response">The answer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The value, or the coded reason it cannot be believed.</returns>
    public async Task<Result<T>> ReadVerifiedAsync<T>(MarketplaceResponse response, CancellationToken ct)
        where T : class
    {
        if (response.StatusCode is < 200 or > 299)
        {
            return Result<T>.Fail(MarketplaceProblemMapper.ToError(response.StatusCode, response.Body));
        }

        var verified = await _checker.VerifyAsync(response.Body, response.Signature, ct);
        if (!verified.IsSuccess)
        {
            return Result<T>.Fail(verified.Error!);
        }

        var parsed = MarketplaceJson.TryParse<T>(response.Body);
        return parsed is null
            ? Result<T>.Fail(MarketplaceClientRefusals.ResponseInvalid)
            : Result<T>.Ok(parsed);
    }
}
