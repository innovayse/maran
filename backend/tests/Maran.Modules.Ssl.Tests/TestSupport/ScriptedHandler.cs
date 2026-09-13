using System.Net;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers a prepared sequence: a status, or a transport
/// failure where the script holds <c>null</c>.
/// </summary>
/// <remarks>
/// Counting calls is the point. A retry policy is only testable by how many times it reached the
/// wire, and asserting on the final status alone cannot tell "retried once and succeeded" from
/// "succeeded first time".
/// </remarks>
public sealed class ScriptedHandler : HttpMessageHandler
{
    /// <summary>The prepared answers, one per call.</summary>
    private readonly HttpStatusCode?[] _script;

    /// <summary>How many times the handler was reached.</summary>
    public int Calls { get; private set; }

    /// <summary>Creates a handler over a prepared script.</summary>
    /// <param name="script">One entry per call; <c>null</c> means a transport failure.</param>
    public ScriptedHandler(HttpStatusCode?[] script)
    {
        _script = script;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var index = Calls;
        Calls++;

        // Past the end of the script is a transport failure rather than a success: a policy that
        // retried more times than the script anticipated must not be rewarded with an OK.
        var status = index < _script.Length ? _script[index] : null;
        if (status is null)
        {
            throw new HttpRequestException("the authority is unreachable");
        }

        return Task.FromResult(new HttpResponseMessage(status.Value));
    }
}
