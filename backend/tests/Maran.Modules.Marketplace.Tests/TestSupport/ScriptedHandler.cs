namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// The innermost HTTP handler of a test: it answers from a script and counts every request. It follows no
/// redirect, so a redirect is seen by the transport exactly as the real handler would deliver it. The count
/// is the instrument the outbound-gate tests read, and a test that expects zero calls is only meaningful
/// because the same handler is shown to count one when a call is made.
/// </summary>
public sealed class ScriptedHandler : HttpMessageHandler
{
    /// <summary>The requests seen so far.</summary>
    private readonly List<RecordedRequest> _requests = [];

    /// <summary>Produces the answer to a request. Replace it to script a test.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
        (_, _) => { return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)); };

    /// <summary>The requests seen so far, oldest first.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            return _requests;
        }
    }

    /// <summary>How many requests reached the handler.</summary>
    public int Count
    {
        get
        {
            return _requests.Count;
        }
    }

    /// <summary>Scripts a synchronous answer.</summary>
    /// <param name="respond">Builds the answer to a request.</param>
    public void Reply(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        Responder = (request, _) => { return Task.FromResult(respond(request)); };
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(
            header => { return header.Key; },
            header => { return string.Join(",", header.Value); },
            StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body));

        return await Responder(request, cancellationToken);
    }
}
