using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Resources;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Services;

/// <summary>Finalizes a validated order with a signing request and downloads the issued chain.</summary>
internal sealed class AcmeOrderFinalizer
{
    /// <summary>The authority's address and the bounds of every wait.</summary>
    private readonly AcmeOptions _options;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the authority's own text goes, since a returned <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates the finalizer.</summary>
    /// <param name="options">The bounds of every wait.</param>
    /// <param name="clock">The injected time source, which bounds the order poll.</param>
    /// <param name="logger">Sink for the authority's diagnostic text.</param>
    public AcmeOrderFinalizer(AcmeOptions options, IClock clock, ILogger logger)
    {
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Finalizes a validated order and downloads the issued chain.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="order">The order object, which names the finalize endpoint.</param>
    /// <param name="orderUrl">The order's own URL, polled until the certificate is ready.</param>
    /// <param name="names">The names the certificate covers, in the order's order; the first is the domain, for the log line.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>The issued material, or a typed failure.</returns>
    public async Task<Result<IssuedCertificate>> FinalizeAsync(
        AcmeSession session,
        JsonElement order,
        string orderUrl,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var domain = names[0];
        var csr = AcmeCertificateRequest.Create(names, out var key);
        using (key)
        {
            var body = JsonSerializer.Serialize(new { csr = AcmeSigner.Base64Url(csr) });
            var finalized = await session.PostAsync(AcmeJson.Text(order, "finalize"), body, cancellationToken);
            if (!finalized.IsSuccess)
            {
                return Fail(
                    domain,
                    "finalize",
                    Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.UpstreamRefused));
            }

            var certificateUrl = await PollOrderAsync(session, orderUrl, cancellationToken);
            if (!certificateUrl.IsSuccess)
            {
                return Result<IssuedCertificate>.Fail(certificateUrl.Error!);
            }

            var chain = await session.PostForTextAsync(certificateUrl.Value, AcmeProtocol.PostAsGet, cancellationToken);
            if (!chain.IsSuccess)
            {
                return Fail(
                    domain,
                    "download",
                    Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.UpstreamRefused));
            }

            return Materialize(chain.Value, key, domain);
        }
    }

    /// <summary>Polls the order until it carries a certificate URL, or the bound is reached.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="orderUrl">The order to poll.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>The certificate URL, or a typed failure.</returns>
    private async Task<Result<string>> PollOrderAsync(
        AcmeSession session,
        string orderUrl,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + _options.ValidationTimeout;

        while (_clock.UtcNow < deadline)
        {
            var polled = await session.PostAsync(orderUrl, AcmeProtocol.PostAsGet, cancellationToken);
            if (!polled.IsSuccess)
            {
                return Result<string>.Fail(
                    Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.UpstreamRefused));
            }

            var certificateUrl = AcmeJson.Text(polled.Value.Body, "certificate");
            if (certificateUrl.Length > 0)
            {
                return Result<string>.Ok(certificateUrl);
            }

            if (string.Equals(AcmeJson.Text(polled.Value.Body, "status"), "invalid", StringComparison.Ordinal))
            {
                return Result<string>.Fail(
                    Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.UpstreamRefused));
            }

            await Task.Delay(_options.PollInterval, cancellationToken);
        }

        return Result<string>.Fail(Error.Of(nameof(ErrorMessages.AcmeValidationTimedOut), ErrorType.Unavailable));
    }

    /// <summary>Turns a downloaded chain and its key into the material an install takes.</summary>
    /// <param name="chainPem">The PEM chain the authority returned, leaf first.</param>
    /// <param name="key">The key the CSR was made with.</param>
    /// <param name="domain">The domain, for the log line if the chain will not parse.</param>
    /// <returns>The material, or <c>AcmeCertificateUnreadable</c>.</returns>
    private Result<IssuedCertificate> Materialize(string chainPem, ECDsa key, string domain)
    {
        try
        {
            using var leaf = X509Certificate2.CreateFromPem(chainPem);
            return Result<IssuedCertificate>.Ok(new IssuedCertificate(
                chainPem,
                key.ExportPkcs8PrivateKeyPem(),
                new DateTimeOffset(leaf.NotAfter.ToUniversalTime(), TimeSpan.Zero)));
        }
        catch (CryptographicException exception)
        {
            // The chain is logged by NEITHER branch: a certificate is public, but this text is
            // whatever the authority actually sent, and a client that logs an unparseable response
            // is a client that logs whatever an unexpected response contains.
            AcmeOrderLog.Failed(_logger, domain, "parse", exception.GetType().Name);
            return Result<IssuedCertificate>.Fail(
                Error.Of(nameof(ErrorMessages.AcmeCertificateUnreadable), ErrorType.UpstreamRefused));
        }
    }

    /// <summary>Logs one failed stage and returns it as the typed failure.</summary>
    /// <param name="domain">The domain the order was for.</param>
    /// <param name="stage">Which step refused.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private Result<IssuedCertificate> Fail(string domain, string stage, Error error)
    {
        AcmeOrderLog.Failed(_logger, domain, stage, error.Code);
        return Result<IssuedCertificate>.Fail(error);
    }
}
